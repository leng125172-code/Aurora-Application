// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetCore.CAP.Dashboard.GatewayProxy;
using DotNetCore.CAP.Dashboard.NodeDiscovery;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

// ReSharper disable UnusedMember.Global

namespace DotNetCore.CAP.Dashboard;

public class RouteActionProvider
{
    private readonly GatewayProxyAgent _agent;
    private readonly IEndpointRouteBuilder _builder;
    private readonly DashboardOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public RouteActionProvider(IEndpointRouteBuilder builder, DashboardOptions options)
    {
        _builder = builder;
        _options = options;
        _serviceProvider = builder.ServiceProvider;
        _agent = _serviceProvider.GetService<GatewayProxyAgent>(); // may be null
    }

    private IDataStorage DataStorage => _serviceProvider.GetRequiredService<IDataStorage>();
    private IMonitoringApi MonitoringApi => DataStorage.GetMonitoringApi();

    public void MapDashboardRoutes()
    {
        var prefixMatch = _options.PathMatch + "/api";

        // 实时指标
        _builder
            .MapGet(prefixMatch + "/metrics-realtime", Metrics)
            .WithTags("CAP")
            .WithName("CAP_GetMetricsRealtime")
            .WithSummary("获取实时指标数据")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 元信息
        _builder
            .MapGet(prefixMatch + "/meta", MetaInfo)
            .WithTags("CAP")
            .WithName("CAP_GetMeta")
            .WithSummary("获取CAP元信息（Broker/Storage）")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 统计概览
        _builder
            .MapGet(prefixMatch + "/stats", Stats)
            .WithTags("CAP")
            .WithName("CAP_GetStats")
            .WithSummary("获取消息统计概览")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 历史指标
        _builder
            .MapGet(prefixMatch + "/metrics-history", MetricsHistory)
            .WithTags("CAP")
            .WithName("CAP_GetMetricsHistory")
            .WithSummary("获取小时级历史指标")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 健康检查（始终匿名）
        _builder
            .MapGet(prefixMatch + "/health", Health)
            .WithTags("CAP")
            .WithName("CAP_Health")
            .WithSummary("健康检查")
            .AllowAnonymous();

        // 已发布消息详情
        _builder
            .MapGet(prefixMatch + "/published/message/{id:long}", PublishedMessageDetails)
            .WithTags("CAP")
            .WithName("CAP_GetPublishedMessageDetail")
            .WithSummary("获取已发布消息详情")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 已接收消息详情
        _builder
            .MapGet(prefixMatch + "/received/message/{id:long}", ReceivedMessageDetails)
            .WithTags("CAP")
            .WithName("CAP_GetReceivedMessageDetail")
            .WithSummary("获取已接收消息详情")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 重新入队已发布消息
        _builder
            .MapPost(prefixMatch + "/published/requeue", PublishedRequeue)
            .WithTags("CAP")
            .WithName("CAP_RequeuePublished")
            .WithSummary("重新入队已发布消息")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 删除已发布消息
        _builder
            .MapPost(prefixMatch + "/published/delete", PublishedDelete)
            .WithTags("CAP")
            .WithName("CAP_DeletePublished")
            .WithSummary("删除已发布消息")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 重新执行已接收消息
        _builder
            .MapPost(prefixMatch + "/received/reexecute", ReceivedRequeue)
            .WithTags("CAP")
            .WithName("CAP_ReexecuteReceived")
            .WithSummary("重新执行已接收消息")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 删除已接收消息
        _builder
            .MapPost(prefixMatch + "/received/delete", ReceivedDelete)
            .WithTags("CAP")
            .WithName("CAP_DeleteReceived")
            .WithSummary("删除已接收消息")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 已发布消息列表（分页）
        _builder
            .MapGet(prefixMatch + "/published/{status}", PublishedList)
            .WithTags("CAP")
            .WithName("CAP_GetPublishedList")
            .WithSummary("分页查询已发布消息列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 已接收消息列表（分页）
        _builder
            .MapGet(prefixMatch + "/received/{status}", ReceivedList)
            .WithTags("CAP")
            .WithName("CAP_GetReceivedList")
            .WithSummary("分页查询已接收消息列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 订阅者列表
        _builder
            .MapGet(prefixMatch + "/subscriber", Subscribers)
            .WithTags("CAP")
            .WithName("CAP_GetSubscribers")
            .WithSummary("获取订阅者列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // 节点列表
        _builder
            .MapGet(prefixMatch + "/nodes", Nodes)
            .WithTags("CAP")
            .WithName("CAP_GetNodes")
            .WithSummary("获取节点列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // K8s 命名空间列表
        _builder
            .MapGet(prefixMatch + "/list-ns", ListNamespaces)
            .WithTags("CAP")
            .WithName("CAP_ListNamespaces")
            .WithSummary("获取K8s命名空间列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // K8s 服务列表
        _builder
            .MapGet(prefixMatch + "/list-svc/{namespace}", ListServices)
            .WithTags("CAP")
            .WithName("CAP_ListServices")
            .WithSummary("获取K8s服务列表")
            .AllowAnonymousIf(_options.AllowAnonymousExplicit, _options.AuthorizationPolicy);

        // Ping 节点健康端点（始终匿名）
        _builder
            .MapGet(prefixMatch + "/ping", PingServices)
            .WithTags("CAP")
            .WithName("CAP_Ping")
            .WithSummary("Ping指定节点的健康端点")
            .AllowAnonymous();
    }

    public async Task Metrics(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var metrics = _serviceProvider.GetRequiredService<CapMetricsEventListener>();
        await httpContext.Response.WriteAsJsonAsync(metrics.GetRealTimeMetrics());
    }

    public async Task MetaInfo(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var cap = _serviceProvider.GetService<CapMarkerService>();
        var broker = _serviceProvider.GetService<CapMessageQueueMakerService>();
        var storage = _serviceProvider.GetService<CapStorageMarkerService>();

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                cap,
                broker,
                storage,
            }
        );
    }

    public async Task Stats(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var result = await MonitoringApi.GetStatisticsAsync();
        await SetServersCountAsync(result);
        await httpContext.Response.WriteAsJsonAsync(result);

        async Task SetServersCountAsync(StatisticsDto dto)
        {
            if (CapCache.Global.TryGet("cap.nodes.count", out var count))
            {
                dto.Servers = (int)count;
            }
            else
            {
                if (_serviceProvider.GetService<ConsulDiscoveryOptions>() != null)
                {
                    var discoveryProvider =
                        _serviceProvider.GetRequiredService<INodeDiscoveryProvider>();
                    var nodes = await discoveryProvider.GetNodes();
                    dto.Servers = nodes.Count;
                }
            }
        }
    }

    public async Task MetricsHistory(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        const string cacheKey = "dashboard.metrics.history";
        if (CapCache.Global.TryGet(cacheKey, out var ret))
        {
            await httpContext.Response.WriteAsJsonAsync(ret);
            return;
        }

        var ps = await MonitoringApi.HourlySucceededJobs(MessageType.Publish);
        var pf = await MonitoringApi.HourlyFailedJobs(MessageType.Publish);
        var ss = await MonitoringApi.HourlySucceededJobs(MessageType.Subscribe);
        var sf = await MonitoringApi.HourlyFailedJobs(MessageType.Subscribe);

        var dayHour = ps
            .Keys.OrderBy(x => x)
            .Select(x => new DateTimeOffset(x).ToUnixTimeSeconds());

        var result = new
        {
            DayHour = dayHour.ToArray(),
            PublishSuccessed = ps.Values.Reverse(),
            PublishFailed = pf.Values.Reverse(),
            SubscribeSuccessed = ss.Values.Reverse(),
            SubscribeFailed = sf.Values.Reverse(),
        };

        CapCache.Global.AddOrUpdate(cacheKey, result, TimeSpan.FromMinutes(10));

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public Task Health(HttpContext httpContext)
    {
        httpContext.Response.WriteAsync("OK");
        return Task.CompletedTask;
    }

    public async Task PublishedMessageDetails(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        if (
            long.TryParse(
                httpContext.GetRouteData().Values["id"]?.ToString() ?? string.Empty,
                out var id
            )
        )
        {
            var message = await MonitoringApi.GetPublishedMessageAsync(id);
            if (message == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await httpContext.Response.WriteAsJsonAsync(message.Content);
        }
        else
        {
            BadRequest(httpContext);
        }
    }

    public async Task ReceivedMessageDetails(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        if (
            long.TryParse(
                httpContext.GetRouteData().Values["id"]?.ToString() ?? string.Empty,
                out var id
            )
        )
        {
            var message = await MonitoringApi.GetReceivedMessageAsync(id);
            if (message == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await httpContext.Response.WriteAsJsonAsync(message.Content);
        }
        else
        {
            BadRequest(httpContext);
        }
    }

    public async Task PublishedRequeue(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }

        foreach (var messageId in messageIds)
        {
            var message = await MonitoringApi.GetPublishedMessageAsync(messageId);
            if (message != null)
                await _serviceProvider.GetRequiredService<IDispatcher>().EnqueueToPublish(message);
        }

        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task PublishedDelete(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }

        foreach (var messageId in messageIds)
            _ = await DataStorage.DeletePublishedMessageAsync(messageId);

        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task ReceivedRequeue(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }

        foreach (var messageId in messageIds)
        {
            var message = await MonitoringApi.GetReceivedMessageAsync(messageId);
            if (message != null)
                await _serviceProvider.GetRequiredService<IDispatcher>().EnqueueToExecute(message);
        }

        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task ReceivedDelete(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }

        foreach (var messageId in messageIds)
            _ = await DataStorage.DeleteReceivedMessageAsync(messageId);

        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task PublishedList(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var routeValue = httpContext.GetRouteData().Values;
        var pageSize = httpContext.Request.Query["perPage"].ToInt32OrDefault(20);
        var pageIndex = httpContext.Request.Query["currentPage"].ToInt32OrDefault(1);
        var name = httpContext.Request.Query["name"].ToString();
        var content = httpContext.Request.Query["content"].ToString();
        var status = routeValue["status"]?.ToString() ?? nameof(StatusName.Succeeded);

        var queryDto = new MessageQueryDto
        {
            MessageType = MessageType.Publish,
            Name = name,
            Content = content,
            StatusName = status,
            CurrentPage = pageIndex - 1,
            PageSize = pageSize,
        };

        var result = await MonitoringApi.GetMessagesAsync(queryDto);

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public async Task ReceivedList(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var routeValue = httpContext.GetRouteData().Values;
        var pageSize = httpContext.Request.Query["perPage"].ToInt32OrDefault(20);
        var pageIndex = httpContext.Request.Query["currentPage"].ToInt32OrDefault(1);
        var name = httpContext.Request.Query["name"].ToString();
        var group = httpContext.Request.Query["group"].ToString();
        var content = httpContext.Request.Query["content"].ToString();
        var status = routeValue["status"]?.ToString() ?? nameof(StatusName.Succeeded);

        var queryDto = new MessageQueryDto
        {
            MessageType = MessageType.Subscribe,
            Group = group,
            Name = name,
            Content = content,
            StatusName = status,
            CurrentPage = pageIndex - 1,
            PageSize = pageSize,
        };

        var result = await MonitoringApi.GetMessagesAsync(queryDto);

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public async Task Subscribers(HttpContext httpContext)
    {
        if (_agent != null && await _agent.Invoke(httpContext))
            return;

        var cache = _serviceProvider.GetRequiredService<MethodMatcherCache>();
        var subscribers = cache.GetCandidatesMethodsOfGroupNameGrouped();

        var result = new List<WarpResult>();

        foreach (var subscriber in subscribers)
        {
            var inner = new WarpResult
            {
                Group = subscriber.Key,
                Values = new List<WarpResult.SubInfo>(),
            };
            foreach (var descriptor in subscriber.Value)
            {
                inner.Values.Add(
                    new WarpResult.SubInfo
                    {
                        Topic = descriptor.TopicName,
                        ImplName = descriptor.ImplTypeInfo.Name,
                        MethodEscaped = HtmlHelper.MethodEscaped(descriptor.MethodInfo),
                    }
                );
            }

            result.Add(inner);
        }

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public async Task Nodes(HttpContext httpContext)
    {
        IList<Node> result = new List<Node>();
        var discoveryProvider = _serviceProvider.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            await httpContext.Response.WriteAsJsonAsync(result);
            return;
        }

        result = await discoveryProvider.GetNodes();

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public async Task ListNamespaces(HttpContext httpContext)
    {
        var discoveryProvider = _serviceProvider.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            await httpContext.Response.WriteAsJsonAsync(new List<string>());
            return;
        }

        var nsList = await discoveryProvider.GetNamespaces(httpContext.RequestAborted);
        if (nsList == null)
            httpContext.Response.StatusCode = 404;
        else
            await httpContext.Response.WriteAsJsonAsync(
                await discoveryProvider.GetNamespaces(httpContext.RequestAborted)
            );
    }

    public async Task ListServices(HttpContext httpContext)
    {
        var @namespace = string.Empty;

        if (httpContext.Request.RouteValues.TryGetValue("namespace", out var val))
            @namespace = val!.ToString();

        var discoveryProvider = _serviceProvider.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            await httpContext.Response.WriteAsJsonAsync(new List<Node>());
            return;
        }

        var result = await discoveryProvider.ListServices(@namespace);

        await httpContext.Response.WriteAsJsonAsync(result);
    }

    public async Task PingServices(HttpContext httpContext)
    {
        var endpoint = httpContext.Request.Query["endpoint"];

        var httpClient = new HttpClient();
        var sw = new Stopwatch();
        try
        {
            sw.Restart();
            var healthEndpoint = endpoint + _options.PathMatch + "/api/health";
            var response = await httpClient.GetStringAsync(healthEndpoint);
            sw.Stop();

            if (response == "OK")
            {
                await httpContext.Response.WriteAsync(sw.ElapsedMilliseconds.ToString());
            }
            else
            {
                httpContext.Response.StatusCode = 501;
                await httpContext.Response.WriteAsync(response);
            }
        }
        catch (HttpRequestException e)
        {
            httpContext.Response.StatusCode = (int)
                e.StatusCode.GetValueOrDefault(HttpStatusCode.BadGateway);
            await httpContext.Response.WriteAsync(e.Message);
        }
        catch (Exception e)
        {
            httpContext.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await httpContext.Response.WriteAsync(e.Message);
        }
    }

    private void BadRequest(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
}

public class WarpResult
{
    public int ChildCount => Values.Count;

    public string Group { get; set; }

    public List<SubInfo> Values { get; set; }

    public class SubInfo
    {
        public string Topic { get; set; }

        public string ImplName { get; set; }

        public string MethodEscaped { get; set; }
    }
}

public static class IntExtension
{
    public static int ToInt32OrDefault(this StringValues value, int defaultValue = 0)
    {
        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}
