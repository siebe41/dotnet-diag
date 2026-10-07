namespace Shop.Api.Models;

public sealed class Order
{
    public string Customer { get; set; }

    public decimal Total { get; set; }
}
