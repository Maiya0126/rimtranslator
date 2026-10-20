using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RimTrans.Core.AI
{
    public class TranslationItem
    {
        public int Id { get; set; }
        public string Source { get; set; } = "";
        public string File { get; set; } = "";
        public string Key { get; set; } = "";
        /// <summary>所属 Mod 名（批量多 Mod 时审阅表分组显示）。</summary>
        public string ModName { get; set; } = "";
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
    /// 匹配优先级：自定义词库 > 云端词库（维护者持续更新）> 本地官方术语库（Data tar 构建）。
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
            IProgress<(int done, int total, string stage)>? progress = null,
            int concurrency = 3,
            CancellationToken ct = default)
        {
            int done = 0;
            // 1. 术语回填（自定义词库 > 云端词库 > 本地官方库）——占整体 10%
            var pending = new List<TranslationItem>();
            foreach (var item in items)
            {
                bool hit = false;
                if (glossary != null && glossary.TryMatchCustom(item.Source, out var g))
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
                else if (glossary != null && glossary.TryMatch(item.Source, out var local))
                {
                    item.GlossaryTranslation = local + "（官方术语库）";
                    item.Final = local;
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

            // 2. AI 并发批量（每批 20 条，默认 3 路并发）——占整体 90%
            const int batchSize = 20;
            var batches = pending.Chunk(batchSize).Select(b => b.ToList()).ToList();
            int aiTotal = pending.Count;
            int aiDone = 0;
            var gate = new SemaphoreSlim(Math.Max(1, concurrency));
            var lockObj = new object();
            var tasks = new List<Task>();
            int index = 0;
            while (index < batches.Count)
            {
                await gate.WaitAsync(ct);
                var list = batches[index++];
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        int batchNo;
                        lock (lockObj) batchNo = batches.IndexOf(list) + 1;
                        int snapshot;
                        lock (lockObj) { snapshot = aiDone; }
                        progress?.Report((10 + snapshot * 90 / aiTotal, 100,
                            $"AI 翻译 {snapshot}/{aiTotal}（批次 {batchNo}/{batches.Count}）"));
                        string userPrompt = BuildBatchPrompt(list, glossary);
                        string? reply = null;
                        for (int attempt = 0; attempt < 2 && reply == null; attempt++)
                        {
                            try
                            {
                                reply = await _client.ChatAsync(userPrompt, SystemPrompt(), maxTokens: 8000);
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
                            ParseBatchReply(reply, list);
                        lock (lockObj) { aiDone += list.Count; }
                        int snap2;
                        lock (lockObj) { snap2 = aiDone; }
                        progress?.Report((10 + snap2 * 90 / aiTotal, 100,
                            $"AI 翻译 {snap2}/{aiTotal}（批次 {batchNo}/{batches.Count}）"));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, ct));
            }
            await Task.WhenAll(tasks.ToArray());
            progress?.Report((100, 100, "AI 翻译完成"));
            return items;
        }

        public async Task<List<TranslationItem>> RetryFailedAsync(
            List<TranslationItem> items,
            Glossary? glossary,
            IProgress<(int done, int total, string stage)>? progress = null,
            int concurrency = 3,
            CancellationToken ct = default)
        {
            var failed = items.Where(i => i.Status == "failed").ToList();
            foreach (var f in failed) { f.Status = "TODO"; f.AiTranslation = null; }
            if (failed.Count > 0)
                await TranslateAsync(failed, glossary, progress, concurrency, ct);
            return items;
        }

        private string SystemPrompt() =>
            "You are a professional game localizer for RimWorld (Ludeon Studios). Translate UI text, def labels, and descriptions for a game mod. " +
            "CONTEXT: RimWorld is a sci-fi colony simulation game. Common setting terms already have established official translations — ALWAYS prefer the official translation over literal translation, e.g. " +
            "colonist(殖民者), mechanoid(机械族), psycast(心灵能力), psychic(心灵), psylink(启灵), psyfocus(精神力), neural heat(心灵熵), raid(袭击), tribal(部落), thrumbo(敲击兽), muffalo(雪牛), warg(座狼), boomalope(爆炸羊), " +
            "glitterworld(闪耀世界), urbworld(都市世界), rimworld(边缘世界), medieval(中世纪), archotech(超凡), deathrest(死眠), xenotype(异种人), gene(基因), " +
            "ideoligion(意识形态), ritual(仪式), royal title(头衔), mechlink(机控中枢), biosculpting(生体塑形), tox gas(毒气). " +
            "When a term is NOT in the provided glossary, translate it the way the official RimWorld localization would (check the game's existing terminology style), not literally. " +
            "For technical identifiers, C# class names, XML tags, or file paths, keep them EXACTLY as-is. " +
            "Grammar rule keys like r_logentry, r_sentence, INITIATOR_nameDef, RECIPIENT_nameDef are RULE SYNTAX, not translatable text — translate only the human-readable parts around them (e.g. \"[INITIATOR_nameDef] spoke.\" → translate 'spoke' only, keep the bracket tag intact). " +
            "Rules: preserve placeholders like {0}, {1}, {PAWN_nameDef} EXACTLY as-is; keep \\n escapes; do not add quotes or explanations; " +
            "output must be natural, concise game text matching the tone of official localization. Reply ONLY with JSON.";

        private string BuildBatchPrompt(List<TranslationItem> items, Glossary? glossary)
        {
            var terms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 云端词库优先（维护者持续更新、更准确），本地官方库次之
            if (CloudGlossary.Loaded)
            {
                foreach (var item in items)
                    foreach (var word in ExtractWords(item.Source))
                        if (CloudGlossary.TryMatch(word, out var z))
                            terms[word] = z;
            }
            if (glossary != null)
            {
                foreach (var item in items)
                    foreach (var word in ExtractWords(item.Source))
                        if (glossary.TryMatch(word, out var t))
                            terms.TryAdd(word, t);
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
