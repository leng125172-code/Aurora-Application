// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DotNetCore.CAP.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

[assembly: InternalsVisibleTo("DotNetCore.CAP.Dashboard.K8s")]

// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP;

public static class CapBuilderExtension
{
    internal static IApplicationBuilder UseCapDashboard(this IApplicationBuilder app)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        var provider = app.ApplicationServices;

        var options = provider.GetService<DashboardOptions>();

        if (options != null)
        {
            var endpointRouteBuilder = (IEndpointRouteBuilder)
                app.Properties["__EndpointRouteBuilder"]!;

            // 仅注册API路由，不再挂载内置前端页面
            new RouteActionProvider(endpointRouteBuilder, options).MapDashboardRoutes();
        }

        return app;
    }

    internal static IEndpointConventionBuilder AllowAnonymousIf(
        this IEndpointConventionBuilder builder,
        bool allowAnonymous,
        params string?[] authorizationPolicies
    )
    {
        if (allowAnonymous)
            return builder.AllowAnonymous();

        var validAuthorizationPolicies = authorizationPolicies
            .Where(policy => !string.IsNullOrEmpty(policy))!
            .ToArray<string>();

        if (!validAuthorizationPolicies.Any())
        {
            throw new InvalidOperationException(
                "If Dashboard Options does not explicitly allow anonymous requests, the Authorization Policy must be configured."
            );
        }

        return builder.RequireAuthorization(validAuthorizationPolicies);
    }
}
