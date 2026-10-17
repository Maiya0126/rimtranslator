using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimTrans.Core.AI
{
    public class TranslationItem
    {
        public int Id { get; set; }
        public string Source { get; set; } = "";
        public string File { get; set; } = "";
        public string Key { get; set; } = "";
        public string? GlossaryMatch { get; set; }
        public string? GlossaryTranslation { get; set; }
        public string? AiTranslation { get; set; }
        public string Final { get; set; } = "";
        public string Status { get; set; } = "TODO"; // TODO / glossary / ai / failed / edited

        /// <summary>审阅界面显示用的状态中文名。</summary>
        public string StatusDisplay => Status switch
        {
            "glossary" => "术语回填",
            "ai" => "AI 翻译",
            "failed" => "失败",
            "edited" => "已编辑",
            _ => "未处理",
        };
    }

    /// <summary>
    /// 批量翻译：术语命中直接回填；其余按批（JSON 编号对齐协议）送 AI；失败重试一次后标记失败。
    /// </summary>
    public class BatchTranslator
    {
        private readonly AiClient _client;
        private readonly string _targetLanguage;

        public BatchTranslator(AiClient client, string targetLanguage)
        {
            _client = client;
            _targetLanguage = targetLanguage;
        }

        public async Task<List<TranslationItem>> TranslateAsync(
            List<TranslationItem> items,
            Glossary? glossary,
            IProgress<(int done, int total, string stage)>? progress = null)
        {
            int done = 0;
            // 1. 术语回填（自定义词库 > 云端 defName 官方词库）——占整体 10%
            var pending = new List<TranslationItem>();
            foreach (var item in items)
            {
                bool hit = false;
                if (glossary != null && glossary.TryMatch(item.Source, out var g))
                {
                    item.GlossaryTranslation = g;
                    item.Final = g;
                    item.Status = "glossary";
                    hit = true;
                }
                else if (CloudGlossary.Loaded && CloudGlossary.TryMatch(item.Source, out var cloud))
                {
                    item.GlossaryTranslation = cloud + "（云端词库）";
                    item.Final = cloud;
                    item.Status = "glossary";
                    hit = true;
                }
                if (!hit) pending.Add(item);
                done++;
                progress?.Report((done * 10 / Math.Max(1, items.Count), 100, $"术语匹配 {done}/{items.Count}"));
            }

            if (pending.Count == 0)
            {
                progress?.Report((100, 100, "术语匹配完成"));
                return items;
            }

            // 2. AI 批量（每批 20 条）——占整体 90%（10% → 100% 线性推进）
            const int batchSize = 20;
            var batches = pending.Chunk(batchSize).ToList();
            int aiTotal = pending.Count;
            int aiDone = 0;
            int batchDone = 0;
            foreach (var batch in batches)
            {
                var list = batch.ToList();
                progress?.Report((10 + aiDone * 90 / aiTotal, 100,
                    $"AI 翻译 {aiDone}/{aiTotal}（批次 {batchDone + 1}/{batches.Count}）"));
                string userPrompt = BuildBatchPrompt(list, glossary);
                string? reply = null;
                for (int attempt = 0; attempt < 2 && reply == null; attempt++)
                {
                    try
                    {
                        string raw = await _client.ChatAsync(userPrompt, SystemPrompt(), maxTokens: 8000);
                        reply = raw;
                    }
                    catch (AiException ex)
                    {
                        if (attempt == 1)
                            foreach (var item in list) { item.Status = "failed"; item.AiTranslation = "错误: " + ex.Message; }
                        else
                            await Task.Delay(500);
                    }
                }
                if (reply != null)
                {
                    ParseBatchReply(reply, list);
                }
                batchDone++;
                aiDone += list.Count;
                progress?.Report((10 + aiDone * 90 / aiTotal, 100,
                    $"AI 翻译 {aiDone}/{aiTotal}（批次 {batchDone}/{batches.Count}）"));
            }
            return items;
        }

        private string SystemPrompt() =>
            "You are a professional game localizer for RimWorld (Ludeon Studios). Translate UI text, def labels, and descriptions for a game mod. " +
            "CONTEXT: RimWorld is a sci-fi colony simulation game. Common setting terms already have established official translations — ALWAYS prefer the official translation over literal translation, e.g. " +
            "colonist(殖民者), mechanoid(机械族), pawn(人形生物/角色), psycast(心灵冲击), psychic(心灵), raid(袭击), tribal(部落民), thrumbo(巨型驼鹿), muffalo(蓬毛兽), warg(步行兽), boomalope(爆炸兽), " +
            "glitterworld(闪耀世界), urbworld(都市世界), rimworld(边缘世界), midworld(中等世界), medieval(中世纪), archotech(远古科技/奥创科技), deathrest(死亡休眠), xenotype(异种人), gene(基因), " +
            "ideoligion(意识形态), ritual(仪式), royal title(头衔), psylink(心灵链接), neural heat(神经热量), tox gas(毒气), mechlink(机械链接), biosculpting(生物塑形). " +
            "When a term is NOT in the provided glossary, translate it the way the official RimWorld localization would (check the game's existing terminology style), not literally. " +
            "For technical identifiers, C# class names, XML tags, or file paths, keep them EXACTLY as-is. " +
            "Grammar rule keys like r_logentry, r_sentence, INITIATOR_nameDef, RECIPIENT_nameDef are RULE SYNTAX, not translatable text — translate only the human-readable parts around them (e.g. \"[INITIATOR_nameDef] spoke.\" → translate 'spoke' only, keep the bracket tag intact). " +
            "Rules: preserve placeholders like {0}, {1}, {PAWN_nameDef} EXACTLY as-is; keep \\n escapes; do not add quotes or explanations; " +
            "output must be natural, concise game text matching the tone of official localization. Reply ONLY with JSON.";

        private string BuildBatchPrompt(List<TranslationItem> items, Glossary? glossary)
        {
            var terms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (glossary != null)
            {
                foreach (var item in items)
                    foreach (var word in ExtractWords(item.Source))
                        if (glossary.TryMatch(word, out var t))
                            terms[word] = t;
            }
            // 云端官方词库：defName 形态的词（如 CamelCase）也作为约束
            if (CloudGlossary.Loaded)
            {
                foreach (var item in items)
                    foreach (var word in ExtractWords(item.Source))
                        if (CloudGlossary.TryMatch(word, out var z))
                            terms.TryAdd(word, z);
            }
            string glossarySection = terms.Count == 0
                ? "\nNo glossary hits. Translate using established official RimWorld terminology for the target language."
                : "\nGlossary (MUST follow exactly):\n" + string.Join("\n", terms.Take(80).Select(kv => $"{kv.Key}={kv.Value}"));

            var arr = items.Select(i => JsonSerializer.Serialize(new { id = i.Id, text = i.Source }));
            return $"Translate each text into {_targetLanguage}. " +
                   "Return a JSON array: [{\"id\":<number>,\"translation\":\"<translated text>\"}]. No other output.\n" +
                   "Input:\n[" + string.Join(",", arr) + "]" + glossarySection;
        }

        private static IEnumerable<string> ExtractWords(string source)
        {
            var words = source.Split(new[] { ' ', ',', '.', ':', ';', '!', '?', '(', ')', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
            {
                string word = w.Trim(' ', '.', ',', '!', '?', ';', ':');
                if (word.Length >= 3) yield return word;
            }
        }

        private static void ParseBatchReply(string reply, List<TranslationItem> items)
        {
            var byId = items.ToDictionary(i => i.Id);
            try
            {
                int start = reply.IndexOf('[');
                int end = reply.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        int id = el.GetProperty("id").GetInt32();
                        string translation = el.GetProperty("translation").GetString() ?? "";
                        if (byId.TryGetValue(id, out var item) && translation.Length > 0)
                        {
                            item.AiTranslation = translation;
                            item.Final = translation;
                            item.Status = "ai";
                        }
                    }
                }
            }
            catch
            {
            }
            foreach (var item in items)
                if (item.Status == "TODO") { item.Status = "failed"; item.AiTranslation = "解析失败"; }
        }
    }
}
