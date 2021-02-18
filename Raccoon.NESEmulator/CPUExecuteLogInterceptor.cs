using Raccoon.Devkits.InterceptProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Raccoon.NESEmulator
{
    public class CPUExecuteLogInterceptor : Interceptor<ICPU>
    {
        private readonly PropertyInfo[] propertyInfos = typeof(ICPU).GetProperties();
        private readonly StringBuilder _stringBuffer = new();
        private long _count = 1;
        public override object? OnExecuting(ICPU target, MethodInfo targetMethod, object?[]? args, Func<object?> next)
        {
            if(targetMethod.Name is nameof(ICPU.ExecuteNextOpcode))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"Cycle {_count++} : ");
                _stringBuffer.Clear();
                foreach (PropertyInfo propertyInfo in propertyInfos)
                {
                    _stringBuffer.Append($"{propertyInfo.Name}:{propertyInfo.GetValue(target)} |");
                }
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(_stringBuffer.ToString());
                Console.ResetColor();
            }
            return next?.Invoke();
        }
    }
}
