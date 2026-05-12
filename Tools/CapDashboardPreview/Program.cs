using DotNetCore.CAP;
using Savorboard.CAP.InMemoryMessageQueue;

var builder = WebApplication.CreateBuilder(args);

// 👇 配置 CAP：纯内存运行，无需数据库/消息队列
builder.Services.AddCap(cap =>
{
    cap.UseInMemoryStorage();
    cap.UseInMemoryMessageQueue();
    cap.UseDashboard(); // 启用仪表盘
});

var app = builder.Build();

// 根路径自动跳转到 Dashboard
app.MapGet("/", () => Results.Redirect("/cap"));

app.Run();
