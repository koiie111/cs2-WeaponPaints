using WeaponPaints.Hud;
static void Check(bool v,string message){if(!v)throw new Exception(message);}
var elapsed=TimeSpan.Zero;var timeout=TimeSpan.FromSeconds(30);var attempts=0;
// Three transient callbacks do not stop a cold operation; success at the next attempt is terminal.
foreach(var transient in new[]{true,true,true,false})
{
 attempts++;var step=HudRetryPolicy.Decide(true,transient,attempts,elapsed,timeout);
 if(!transient){Check(step.Decision==HudRetryDecision.Complete,"successful/permanent outcome must terminate");break;}
 Check(step.Decision==HudRetryDecision.Retry,"transient outcome stopped before deadline");elapsed+=step.Delay;
}
Check(attempts==4 && elapsed==TimeSpan.FromSeconds(7),"deferred outcomes must keep retrying past two seconds");
Check(HudRetryPolicy.Decide(true,false,1,TimeSpan.Zero,timeout).Decision==HudRetryDecision.Complete,"permanent failure retried");
Check(HudRetryPolicy.Decide(false,true,1,TimeSpan.Zero,timeout).Decision==HudRetryDecision.Ignore,"cancelled/stale callback retried");
Check(HudRetryPolicy.Decide(true,true,2,timeout,timeout).Decision==HudRetryDecision.Timeout,"deadline not enforced");
Check(HudRetryPolicy.Decide(true,false,2,timeout,timeout).Decision==HudRetryDecision.Timeout,"late completion exceeded deadline");
Check(HudRetryPolicy.Decide(true,false,1,TimeSpan.FromSeconds(1),timeout).Decision==HudRetryDecision.Complete,"non-retryable partial refresh must complete immediately");
Check(HudRetryPolicy.Decide(true,false,1,timeout,timeout).Decision==HudRetryDecision.Timeout,"late non-retryable partial refresh must respect the deadline");
Check(HudRetryPolicy.Decide(true,true,2,TimeSpan.FromSeconds(29),timeout).Decision==HudRetryDecision.WaitDeadline,"retry scheduled past deadline");
Check(HudRetryPolicy.Decide(true,true,HudRetryPolicy.MaxAttempts,TimeSpan.FromSeconds(27),TimeSpan.FromSeconds(90)).Decision==HudRetryDecision.WaitDeadline,"bounded attempts must await deadline, not fail early");
foreach(var seconds in new[]{5,30,90})
{
    var age=TimeSpan.Zero;var count=0;var budget=TimeSpan.FromSeconds(seconds);HudRetryStep step;
    do
    {
        step=HudRetryPolicy.Decide(true,true,++count,age,budget);
        if(step.Decision==HudRetryDecision.Retry) age+=step.Delay;
    } while(step.Decision==HudRetryDecision.Retry);
    Check(step.Decision==HudRetryDecision.WaitDeadline && age<budget && count<=HudRetryPolicy.MaxAttempts,
        "transient outcome must wait for deadline with bounded attempts");
}
Console.WriteLine("PASS: transient backoff, bounded deadline, terminal failures and stale callbacks");
