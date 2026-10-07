using Newtonsoft.Json;

var order = new { Id = 42, Customer = "Contoso" };
Console.WriteLine(JsonConvert.SerializeObject(order));
