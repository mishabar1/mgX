using MG.Server.BL;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DataRepository, DataRepository>();
builder.Services.AddSingleton<SignalIRClient, SignalIRClient>();


builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "MGX",
                      policy =>
                      {
                          policy
                          .SetIsOriginAllowed((host) => true)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                          ;
                      });
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
//else
//{
//    app.UseDefaultFiles();
//    app.UseStaticFiles();
//}

app.UseHttpsRedirection();
app.UseCors("MGX");
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notifications");

app.Run();
