using Shop.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderSerializer>();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<ShopDbContext>();

var app = builder.Build();
app.Run();
