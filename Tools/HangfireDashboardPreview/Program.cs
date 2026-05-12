using Hangfire;
using Hangfire.MemoryStorage;

var builder = WebApplication.CreateBuilder(args);

// 👇 配置 Hangfire：纯内存运行，无需数据库
builder.Services.AddHangfire(config =>
{
    config.UseMemoryStorage();
});

// 启动 Hangfire 服务端（用于展示仪表盘）
builder.Services.AddHangfireServer();

var app = builder.Build();

// 启用 Hangfire Dashboard，允许所有来源访问（仅用于预览）
app.UseHangfireDashboard(
    "/hangfire",
    new DashboardOptions
    {
        Authorization = [], // 不鉴权，方便本地预览
    }
);

// 根路径自动跳转到 Dashboard
app.MapGet("/", () => Results.Redirect("/hangfire"));
app.Run();
