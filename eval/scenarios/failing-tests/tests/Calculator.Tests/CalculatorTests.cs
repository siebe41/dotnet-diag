using Calculator;

namespace Calculator.Tests;

public class CalculatorTests
{
    [Fact]
    public void Divide_returns_fraction() => Assert.Equal(2.5, MathEngine.Divide(5, 2));

    [Fact]
    public void Last_after_clear_does_not_throw()
    {
        var history = new History();
        history.Clear();
        Assert.Equal(0, history.Last());
    }

    [Fact]
    public void Divide_even_numbers() => Assert.Equal(2, MathEngine.Divide(4, 2));
}
