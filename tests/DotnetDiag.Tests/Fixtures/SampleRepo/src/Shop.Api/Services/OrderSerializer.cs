using System;
using System.Text;
using Newtonsoft.Json;
using Shop.Api.Models;

namespace Shop.Api.Services;

public sealed class OrderSerializer
{
    private readonly Encoding _encoding = Encoding.UTF8;

    public string Serialize(Order order)
    {
        var json = JsonConvert.SerializeObject(order);
        return json;
    }

    public Order? Deserialize(string json)
    {
        return JsonConvert.DeserializeObject<Order>(json);
    }
}
