// This file is part of Hangfire. Copyright © 2019 Hangfire OÜ.
//
// Hangfire is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3
// of the License, or any later version.
//
// Hangfire is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

using Hangfire.Annotations;
using Microsoft.AspNetCore.Routing;

namespace Hangfire
{
    /// <summary>
    /// 提供 Hangfire REST API 的 Endpoint 路由注册扩展方法。
    /// </summary>
    public static class HangfireEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// 在指定路径下注册所有 Hangfire 监控 REST API 端点。
        /// 默认路径前缀为 /api/hangfire
        /// </summary>
        /// <param name="endpoints">ASP.NET Core 端点路由构建器</param>
        /// <param name="pathPrefix">API 路径前缀，默认 /api/hangfire</param>
        public static IEndpointRouteBuilder MapHangfireApi(
            [NotNull] this IEndpointRouteBuilder endpoints,
            string pathPrefix = "/api/hangfire"
        )
        {
            var provider = new Api.HangfireRouteActionProvider(endpoints, pathPrefix);
            provider.MapApiRoutes();
            return endpoints;
        }
    }
}
