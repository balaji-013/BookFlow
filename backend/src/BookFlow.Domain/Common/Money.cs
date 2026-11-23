namespace BookFlow.Domain.Common;

public static class Money
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal RoundRupee(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
