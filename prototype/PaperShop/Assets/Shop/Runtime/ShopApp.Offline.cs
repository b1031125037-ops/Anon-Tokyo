using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public OfflineIncomeReport LatestOfflineIncome { get; private set; }
        bool offlineTrackingReady, offlineAway;
        float nextOnlineSave;
        Vector2 offlineScroll;
        string offlineSaveWarning = "";
        // A failed save cannot consume a reward. Retry only this completed
        // absence, never extend it across subsequent foreground play.
        long pendingOfflineUntilUtc;
        static long UtcSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ShopSession RecoverOfflineSession(ShopSession session)
        {
            if (verifyMode || session == null) return session;
            long now = UtcSeconds();
            if (TryCommitOfflineIncome(session.layout, session.economy, now, ShopSession.SavePath,
                out var settled, out var report, out string reason))
            {
                session.economy = settled;
                LatestOfflineIncome = report;
            }
            else
            {
                pendingOfflineUntilUtc = now;
                offlineSaveWarning = "离线结算尚未保存，将保留该时段并重试：" + reason;
            }
            return session;
        }

        bool TryCommitOfflineIncome(ShopLayout layout, ShopEconomy source, long now, string path,
            out ShopEconomy settled, out OfflineIncomeReport report, out string reason, long rebaseUtc = 0)
        {
            settled = source;
            var draft = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(source));
            if (!draft.TryApplyOfflineIncome(catalog, layout, now, out report, out reason)) return false;
            // A delayed retry must discard the intervening online time in the
            // same write that grants its old reward, not in a later save.
            if (rebaseUtc > 0 && !CaptureBaselineAtHighWater(draft, layout, rebaseUtc, out reason)) return false;
            try
            {
                // Persist goods, income and the consumed UTC interval together.
                ShopSession.SaveToPath(layout, draft, catalog, path);
                settled = draft;
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        void InitializeOfflineTracking()
        {
            if (verifyMode || blockedSave) return;
            offlineTrackingReady = true;
            SaveTrackedSession(false);
            offlineAway = false;
            nextOnlineSave = Time.unscaledTime + 30f;
            if (!string.IsNullOrEmpty(offlineSaveWarning)) LastMessage = offlineSaveWarning;
            else if (LatestOfflineIncome != null &&
                (LatestOfflineIncome.elapsedSeconds >= 60 || LatestOfflineIncome.clockWentBack))
                OpenOfflineSummary();
        }

        bool SaveTrackedSession(bool announce)
        {
            if (blockedSave) return false;
            if (pendingOfflineUntilUtc > 0 && !RetryPendingOfflineIncome()) return false;
            var snapshot = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            // A background quit must retain the original departure, not erase
            // the time spent away by moving its timestamp to the quit event.
            bool captured = false;
            if (!offlineAway && !(captured = CaptureBaselineAtHighWater(snapshot, Layout, UtcSeconds(), out string reason)))
            {
                LastMessage = "无法更新离线记录，未覆盖存档：" + reason;
                return false;
            }
            try
            {
                ShopSession.Save(Layout, snapshot, catalog);
                Economy = snapshot; dirty = false;
                if (announce) LastMessage = "店铺与离线记录已保存。";
                return true;
            }
            catch (Exception ex)
            {
                // The timestamp itself is not a reward. Keep the exact live
                // departure even when storage is unavailable, rather than
                // charging the last autosave's foreground seconds as absence.
                if (captured) Economy = snapshot;
                LastMessage = "保存失败，当前进度仍保留：" + ex.Message;
                return false;
            }
        }

        bool CaptureBaselineAtHighWater(ShopEconomy snapshot, ShopLayout layout, long now, out string reason)
        {
            // Refresh eligibility and goods after a clock rollback while
            // retaining the future high-water time. No earlier time can earn.
            return snapshot.CaptureOfflineBaseline(catalog, layout,
                Math.Max(now, snapshot.offlineLastCheckUtcSeconds), out reason);
        }

        bool RetryPendingOfflineIncome()
        {
            if (pendingOfflineUntilUtc <= 0) return true;
            if (!TryCommitOfflineIncome(Layout, Economy, pendingOfflineUntilUtc, ShopSession.SavePath,
                out var settled, out var report, out string reason, UtcSeconds()))
            {
                LastMessage = "离线结算尚未保存，将保留该时段并重试：" + reason;
                return false;
            }
            Economy = settled;
            LatestOfflineIncome = report;
            pendingOfflineUntilUtc = 0;
            offlineSaveWarning = "";
            if (report.elapsedSeconds >= 60 || report.clockWentBack) OpenOfflineSummary();
            return true;
        }

        void TickOfflineTracking()
        {
            if (!offlineTrackingReady || verifyMode || blockedSave) return;
            if (Time.unscaledTime < nextOnlineSave) return;
            SaveTrackedSession(false);
            nextOnlineSave = Time.unscaledTime + 30f;
        }

        void OnOfflineActivityChanged() { /* A running slot never becomes an offline session on focus changes. */ }

        public void OpenOfflineSummary()
        {
            if (panelKey != "offline") OpenPanel("offline");
        }

        // Isolated acceptance uses the same save-before-award path as startup.
        public bool SettleOfflineForVerification(long utcSeconds, string path)
        {
            if (!verifyMode) return false;
            if (!TryCommitOfflineIncome(Layout, Economy, utcSeconds, path,
                out var settled, out var report, out string reason))
            { LastMessage = reason; return false; }
            Economy = settled; LatestOfflineIncome = report; return true;
        }

        void DrawOfflineSummary(Rect box)
        {
            float x = box.x + 22;
            var report = LatestOfflineIncome;
            GUI.Label(new Rect(x, box.y + 60, 570, 48),
                report == null ? "本次尚无离线结算记录。" :
                "离开 " + FormatOfflineTime(report.elapsedSeconds) + " · 计入 " + FormatOfflineTime(report.creditedSeconds) +
                "\n售出 " + report.unitsSold + " 件 · 收入 +" + report.income, heading);
            GUI.Label(new Rect(x, box.y + 121, 570, 62),
                "每小时按离开时可售现货的 5% 结算，最多计算 24 小时。\n只在离开时营业且收银可用时生效；不足一件的进度保留。\n实际扣除货架与仓库现货，不提前使用未到货或已预留商品。", small);
            var lines = report?.lines;
            offlineScroll = GUI.BeginScrollView(new Rect(x, box.y + 196, 570, 118), offlineScroll,
                new Rect(0, 0, 545, Mathf.Max(118, (lines?.Count ?? 0) * 44)));
            if (lines != null)
                for (int i = 0; i < lines.Count; i++)
                {
                    DrawProductIcon(new Rect(4,i*44,36,36),lines[i].productId);
                    GUI.Label(new Rect(50, i * 44+4, 490, 27),
                        ProductName(lines[i].productId) + "  × " + lines[i].quantity + "    +" + lines[i].income, body);
                }
            GUI.EndScrollView();
            GUI.Label(new Rect(x, box.y + 326, 570, 55),
                report?.message ?? "首次启用只建立时间记录，不追溯旧版离线时长。", small);
            GUI.Label(new Rect(x, box.y + 386, 570, 42),
                "累计离线售出 " + Economy.offlineUnitsSold + " 件 · 累计收入 " + Economy.offlineRevenue +
                (Economy.testMode ? "\n金币无限。" : ""), small);
        }

        static string FormatOfflineTime(long seconds) =>
            (seconds / 3600) + "小时 " + ((seconds % 3600) / 60) + "分 " + (seconds % 60) + "秒";
    }
}
