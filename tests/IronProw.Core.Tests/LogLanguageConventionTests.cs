using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace IronProw.Core.Tests;

public class LogLanguageConventionTests
{
    [Fact]
    public void No_LoggerMessage_attribute_contains_Hangul()
    {
        var asm = typeof(SelectingChatClient).Assembly;
        var hangul = new Regex("[가-힣]");
        var offenders = new List<string>();

        foreach (var type in asm.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        foreach (var attr in method.GetCustomAttributesData())
        {
            if (!attr.AttributeType.Name.Contains("LoggerMessage", StringComparison.Ordinal)) continue;
            foreach (var arg in attr.NamedArguments)
                if (arg.MemberName == "Message" && arg.TypedValue.Value is string s && hangul.IsMatch(s))
                    offenders.Add($"{type.Name}.{method.Name}: {s}");
        }

        offenders.Should().BeEmpty();
    }
}
