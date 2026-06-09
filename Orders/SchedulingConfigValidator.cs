namespace Orders;

public static class SchedulingConfigValidator
{
    public static void Validate(SchedulingOptions options)
    {
        if (options.SessionSlidingDays <= 0) throw new InvalidOperationException("SessionSlidingDays must be positive.");
        if (options.DefaultMinBusinessDays < 0) throw new InvalidOperationException("DefaultMinBusinessDays must be non-negative.");

        foreach (var rule in options.WorkRules)
        {
            if (rule.MinBusinessDays < 0)
                throw new InvalidOperationException("Work-rule MinBusinessDays must be non-negative.");
        }
    }
}
