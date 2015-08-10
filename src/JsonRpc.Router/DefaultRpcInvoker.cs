﻿using JsonRpc.Router.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace JsonRpc.Router
{
	public class DefaultRpcInvoker : IRpcInvoker
	{
		public List<RpcResponseBase> InvokeBatchRequest(List<RpcRequest> requests, RpcRoute route)
		{
			var invokingTasks = new List<Task<RpcResponseBase>>();
			foreach (RpcRequest request in requests)
			{
				Task<RpcResponseBase> invokingTask = Task.Run(() => this.InvokeRequest(request, route));
				invokingTasks.Add(invokingTask);
			}

			Task.WaitAll(invokingTasks.Cast<Task>().ToArray());

			List<RpcResponseBase> responses = invokingTasks
				.Select(t => t.Result)
				.Where(r => r != null)
				.ToList();

			return responses;
		}

		public RpcResponseBase InvokeRequest(RpcRequest request, RpcRoute route)
		{
			RpcResponseBase rpcResponse;
			try
			{
				if (request == null)
				{
					throw new ArgumentNullException(nameof(request));
				}
				if (!string.Equals(request.JsonRpcVersion, "2.0"))
				{
					throw new RpcInvalidRequestException("Request must be jsonrpc version '2.0'");
				}

				object[] parameterList;
				RpcMethod rpcMethod = this.GetMatchingMethod(route, request, out parameterList);

				object result = rpcMethod.Invoke(parameterList);

				rpcResponse = new RpcResultResponse(request.Id, result);
			}
			catch (RpcException ex)
			{
				RpcError error = new RpcError(ex);
				rpcResponse = new RpcErrorResponse(request?.Id, error);
			}
#if DEBUG
			catch (Exception ex)
			{
				RpcUnknownException exception = new RpcUnknownException(ex.Message);
#else
			catch (Exception)
			{
				string message = "An internal server error has occurred";
				RpcUnknownException exception = new RpcUnknownException(message);
#endif
				RpcError error = new RpcError(exception);
				rpcResponse = new RpcErrorResponse(request?.Id, error);
			}

			if (request?.Id != null)
			{
				//Only give a response if there is an id
				return rpcResponse;
			}
			return null;
		}

		private RpcMethod GetMatchingMethod(RpcRoute route, RpcRequest request, out object[] parameterList)
		{
			if (route == null)
			{
				throw new ArgumentNullException(nameof(route));
			}
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}
			List<RpcMethod> methods = DefaultRpcInvoker.GetRpcMethods(route);

			methods = methods
				.Where(m => string.Equals(m.Method, request.Method, StringComparison.OrdinalIgnoreCase))
				.ToList();


			RpcMethod rpcMethod = null;
			parameterList = null;
			foreach (RpcMethod method in methods)
			{
				bool matchingMethod;
				if (request.ParameterMap != null)
				{
					matchingMethod = method.HasParameterSignature(request.ParameterMap, out parameterList);
				}
				else
				{
					matchingMethod = method.HasParameterSignature(request.ParameterList);
					parameterList = request.ParameterList;
				}
				if (matchingMethod)
				{
					if (rpcMethod != null) //If already found a match
					{
						throw new RpcAmbiguousMethodException();
					}
					rpcMethod = method;
				}
			}

			if (rpcMethod == null)
			{
				throw new RpcMethodNotFoundException();
			}
			return rpcMethod;
		}

		private static List<RpcMethod> GetRpcMethods(RpcRoute route)
		{
			List<RpcMethod> rpcMethods = new List<RpcMethod>();
			foreach (Type type in route.GetClasses())
			{
				MethodInfo[] publicMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				foreach (MethodInfo publicMethod in publicMethods)
				{
					RpcMethod rpcMethod = new RpcMethod(type, route, publicMethod);
					rpcMethods.Add(rpcMethod);
				}
			}
			return rpcMethods;
		}

	}
}
