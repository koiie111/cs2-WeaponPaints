using WeaponPaints.Hud;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
var m=new HudMetrics();
Check(m.BeginApply(false)==0,"disabled diagnostics must not start a trace");
m.RecordApply(false,HudApplyPhase.StoreError);m.RecordRefreshFailures(false,0,["weapon-private-value"]);
Check(m.ApplyPhaseCount(HudApplyPhase.StoreError)==0 && m.ApplyPhaseCount(HudApplyPhase.RefreshFailOther)==0,"disabled diagnostics must not record events");
var cold=m.BeginApply(true);m.RecordApply(true,HudApplyPhase.StoreStarted,cold);m.RecordApply(true,HudApplyPhase.StoreSaved,cold);
m.RecordApply(true,HudApplyPhase.CwStarted,cold);m.RecordApply(true,HudApplyPhase.CwSucceeded,cold);
m.RecordApply(true,HudApplyPhase.RefreshStarted,cold);m.RecordRefreshFailures(true,cold,["оружие и перчатки","значок","private-value"]);
m.RecordApply(true,HudApplyPhase.RefreshFailed,cold);
var first=m.ApplyDiagnostics();
Check(first.Contains("apply_clickaccepted=1") && first.Contains("apply_storesaved=1") && first.Contains("apply_refreshfailed=1"),"cold phase counts missing");
Check(first.Contains("last_apply_phase=RefreshFailed") && first.Contains("last_refresh_failure_mask=56"),"bounded subsystem codes missing");
Check(first.Contains("last_store_ms=") && first.Contains("last_cw_ms=") && first.Contains("last_refresh_ms=") && !first.Contains("private-value"),"timing fields or privacy guard missing");
var warm=m.BeginApply(true);m.RecordApply(true,HudApplyPhase.ValidationRejected,warm,HudSkinValidationIssue.StickerPosition);
m.RecordApply(true,HudApplyPhase.CwTimeout,cold);
var last=m.ApplyDiagnostics();
Check(last.Contains("apply_clickaccepted=2") && last.Contains("last_apply_phase=ValidationRejected") && last.Contains("last_apply_reason=StickerPosition"),"stale callback overwrote newest trace or field reason missing");
Check(last.Contains("last_refresh_failure_mask=0"),"previous trace leaked subsystem result");
Parallel.For(0,1000,_=>m.RecordApply(true,HudApplyPhase.ClickBlocked));
Check(m.ApplyPhaseCount(HudApplyPhase.ClickBlocked)==1000,"concurrent aggregate events were lost");
Console.WriteLine("PASS: diagnostics off, phase outcomes, timings, bounded reasons, stale trace and concurrent counters");

var timings = new HudMetrics();
// Warm the disabled calls before checking their allocation budget.
using (timings.TimeMainThreadClick(false,"private-button"))
using (timings.TimeMainThread(false,HudMainThreadPhase.RenderView)) { }
long allocated = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++)
{
    using var click = timings.TimeMainThreadClick(false,"private-button");
    using var phase = timings.TimeMainThread(false,HudMainThreadPhase.RenderView);
}
Check(GC.GetAllocatedBytesForCurrentThread() == allocated,"disabled scopes allocated memory");
var off = timings.MainThreadDiagnostics("summary");
Check(off.Contains("click_apply_count=0") && off.Contains("main_renderview_count=0") && timings.MainThreadDiagnostics("max").Contains("click_max_class=none"),"disabled scopes recorded a sample");

// An ownership-rejected Apply must not consume the first authorized sample.
using (timings.TimeMainThreadClick(true,"apply"))
using (timings.TimeMainThread(true,HudMainThreadPhase.ClickAuthorize)) { }
Check(timings.MainThreadDiagnostics("firstapply").Contains("click_firstauthorizedapply_class=none"),"blocked Apply consumed first authorized sample");
using (timings.TimeMainThreadClick(true,"apply"))
{
    using (timings.TimeMainThread(true,HudMainThreadPhase.ClickAuthorize)) { }
    timings.MarkMainThreadClickAuthorized();
    using (timings.TimeMainThread(true,HudMainThreadPhase.ClickDispatch))
    using (timings.TimeMainThread(true,HudMainThreadPhase.ApplySkin))
    {
        using (timings.TimeMainThread(true,HudMainThreadPhase.ApplyValidation)) { }
        using (timings.TimeMainThread(true,HudMainThreadPhase.ApplyCustomSnapshot)) { }
        using (timings.TimeMainThread(true,HudMainThreadPhase.ApplySavingRender))
        {
            using var render = timings.TimeMainThread(true,HudMainThreadPhase.RenderTotal);
            using (timings.TimeMainThread(true,HudMainThreadPhase.RenderView)) { }
            using (timings.TimeMainThread(true,HudMainThreadPhase.RenderFrame)) { }
            using (timings.TimeMainThread(true,HudMainThreadPhase.RenderSubmit)) { }
        }
    }
}
var summary = timings.MainThreadDiagnostics("summary");
var firstClick = timings.MainThreadDiagnostics("firstapply");
Check(summary.Contains("click_apply_count=2") && firstClick.Contains("click_firstauthorizedapply_class=Apply") && timings.MainThreadDiagnostics("max").Contains("click_max_class=Apply"),"first authorized Apply or peak classification missing");
Check(summary.Contains("main_renderview_count=1") && summary.Contains("main_rendersubmit_count=1") && firstClick.Contains("click_firstauthorizedapply_applycustomsnapshot_ms="),"nested synchronous phase attribution missing");
using (timings.TimeMainThreadClick(true,"state_0"))
using (timings.TimeMainThread(true,HudMainThreadPhase.ClickDispatch)) { }
Check(timings.MainThreadDiagnostics("last").Contains("click_last_class=StatePrimary"),"result acknowledgment must be distinguishable from Apply");
try
{
    using var click = timings.TimeMainThreadClick(true,"private-button-value");
    using var phase = timings.TimeMainThread(true,HudMainThreadPhase.RenderSubmit);
    throw new InvalidOperationException("injected");
}
catch (InvalidOperationException) { }
for (int i = 0; i < 2000; i++)
{
    using var click = timings.TimeMainThreadClick(true,"next");
    using var phase = timings.TimeMainThread(true,HudMainThreadPhase.ClickDispatch);
}
var afterMany = timings.MainThreadDiagnostics("summary");
Check(afterMany.Contains("click_paging_count=2000") && afterMany.Contains("click_other_count=1") && timings.MainThreadDiagnostics("last").Contains("click_last_class=Paging"),"exception cleanup or fixed button classes failed");
Check(timings.MainThreadDiagnostics("firstapply") == firstClick,"first authorized Apply sample was overwritten by subsequent clicks");
foreach (var selector in new[] { "summary", "firstapply", "max", "last" })
{
    var report = timings.MainThreadDiagnostics(selector);
    Check(!report.Contains("private-button") && System.Text.Encoding.UTF8.GetByteCount(report) < 2048,"diagnostic response leaked input or exceeded size budget");
}
// Exercise maximum-width counters and tick values, not only small test values.
foreach (var name in new[] { "_phaseCounts", "_phaseMaximums", "_clickCounts" })
{
    var field = typeof(HudMetrics).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    Array.Fill((long[])field.GetValue(timings)!, long.MaxValue);
}
Check(System.Text.Encoding.UTF8.GetByteCount(timings.MainThreadDiagnostics("summary")) < 2048,"maximum-width summary exceeds size budget");
try { timings.MainThreadDiagnostics("invalid"); throw new Exception("invalid selector accepted"); }
catch (ArgumentOutOfRangeException) { }
Console.WriteLine("PASS: disabled zero-allocation, authorized first Apply, phase attribution, bounded selectors and privacy");
