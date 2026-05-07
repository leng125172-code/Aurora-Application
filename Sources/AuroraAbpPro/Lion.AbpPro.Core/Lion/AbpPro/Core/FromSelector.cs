namespace Lion.AbpPro.Core;

public class FromSelector<TValue, TLabel>
{
    public FromSelector(TValue value, TLabel label)
    {
        Value = value;
        Label = label;
    }

    public TValue Value { get; set; }
    public TLabel Label { get; set; }
}
