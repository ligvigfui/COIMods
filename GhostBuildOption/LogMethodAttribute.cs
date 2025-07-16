namespace GhostBuildOption;
using MethodDecorator.Fody.Interfaces;
using System;
using System.Reflection;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public class LogMethodAttribute : Attribute, IMethodDecorator
{
    private MethodBase _method;
    private object[] _args;

    public void Init(object instance, MethodBase method, object[] args)
    {
        _method = method;
        _args = args;
    }

    public void OnEntry()
    {
        Logger.Info($"Entering {_method.Name} with args: {string.Join(", ", _args)}");
    }

    public void OnExit()
    {
        // You can't get the return value here, see below for workaround
        Logger.Info($"Exiting {_method.Name}");
    }

    public void OnException(Exception exception)
    {
        Logger.Info($"Exception in {_method.Name}: {exception}");
    }
}