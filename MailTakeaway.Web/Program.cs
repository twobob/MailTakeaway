using MailTakeaway.Core.Models;
using MailTakeaway.Core.Services;
using MailTakeaway.Web;

var builder = WebApplication.CreateBuilder(args);

// Configure port
var port = builder.Configuration.GetValue<int?>("Port") ?? 9999;
builder.WebHost.UseUrls($"http://localhost:{port}");

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<EmailIndexService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Initialize email index on startup
var indexService = app.Services.GetRequiredService<EmailIndexService>();
_ = Task.Run(async () => await indexService.InitializeAsync());

Console.WriteLine($"Server running on http://localhost:{port}");
app.Run();
