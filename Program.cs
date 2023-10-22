using MG.Server.BL;
using MG.Server.Database;
using MG.Server.Services;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DataRepository, DataRepository>();

builder.Services.AddScoped<GameBL, GameBL>();
builder.Services.AddScoped<UserBL, UserBL>();

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

builder.Services.AddControllers().AddJsonOptions(options => {
    //options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.MaxDepth = 10;
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
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

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
           Path.Combine(builder.Environment.ContentRootPath, "wwwroot")),
    RequestPath = "",
    ServeUnknownFileTypes = true
});
app.UseSpa(spa =>
{
    spa.Options.SourcePath = "wwwroot";


    //if (app.Environment.IsDevelopment())
    //{
    //    spa.UseAngularCliServer(npmScript: "start");
    //}

});



app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notifications");

app.Run();
