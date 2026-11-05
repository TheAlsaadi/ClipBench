using ClipBench.Core.Detection;

namespace ClipBench.Core.Tests.Detection;

public class CodeDetectorTests
{
    [Theory]
    [InlineData("using System;\nnamespace Demo;\npublic class Greeter\n{\n    public string Hello(string name) => $\"Hello {name}\";\n}", "C#")]
    [InlineData("var total = items.Select(x => x.Price).Sum();\nConsole.WriteLine(total);", "C#")]
    [InlineData("const add = (a, b) => a + b;\nconsole.log(add(1, 2));", "JavaScript")]
    [InlineData("function greet(name) {\n  return 'Hello ' + name;\n}", "JavaScript")]
    [InlineData("import React from 'react';\nconst App = () => <div />;", "JavaScript")]
    [InlineData("interface User {\n  name: string;\n  age: number;\n}\nconst u: User = { name: 'a', age: 1 };", "TypeScript")]
    [InlineData("def greet(name):\n    print(f'Hello {name}')\n    return name\n", "Python")]
    [InlineData("import os\nfrom pathlib import Path\n\nclass Thing:\n    def __init__(self):\n        self.x = 1", "Python")]
    [InlineData("public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"Hi\");\n    }\n}", "Java")]
    [InlineData("#include <stdio.h>\nint main(void) {\n    printf(\"hi\\n\");\n    return 0;\n}", "C/C++")]
    [InlineData("#include <iostream>\nint main() {\n    std::cout << \"hi\" << std::endl;\n}", "C/C++")]
    [InlineData("package main\n\nimport \"fmt\"\n\nfunc main() {\n    fmt.Println(\"hi\")\n}", "Go")]
    [InlineData("fn main() {\n    let mut x = 5;\n    println!(\"{}\", x);\n}", "Rust")]
    [InlineData("SELECT id, name FROM users WHERE age > 30 ORDER BY name;", "SQL")]
    [InlineData("INSERT INTO users (id, name) VALUES (1, 'a');", "SQL")]
    [InlineData("CREATE TABLE users (id INT PRIMARY KEY, name TEXT);", "SQL")]
    [InlineData("#!/bin/bash\nset -e\necho \"hello\"\nls -la | grep foo", "Shell")]
    [InlineData("sudo apt update && sudo apt install -y git", "Shell")]
    [InlineData("Get-ChildItem -Path C:\\ -Recurse | Where-Object { $_.Length -gt 1MB }", "PowerShell")]
    [InlineData("$env:Path += ';C:\\Tools'\nWrite-Host 'done'", "PowerShell")]
    [InlineData("<!DOCTYPE html>\n<html>\n<body><div class=\"a\">Hi</div></body>\n</html>", "HTML")]
    [InlineData("<div class=\"card\"><span>Hi</span></div>", "HTML")]
    [InlineData("<?xml version=\"1.0\"?>\n<root><item id=\"1\"/></root>", "XML")]
    [InlineData("{\"name\": \"a\", \"items\": [1, 2, 3], \"ok\": true}", "JSON")]
    [InlineData("[1, 2, 3]", "JSON")]
    [InlineData(".card {\n  color: red;\n  margin: 0 auto;\n}", "CSS")]
    public void Code_in_a_known_language_is_recognised_and_named(string text, string language)
    {
        CodeDetector.Guess(text).ShouldBe(language);
    }

    [Theory]
    [InlineData("Dear Sam, thank you for the invoice. I will import the goods from the warehouse and call the class representative.")]
    [InlineData("Meeting notes: budget review and hiring plan")]
    [InlineData("Invoice 2026-10 for ACME Corp")]
    [InlineData("Remember to buy milk, eggs and bread; also the function of the new class is unclear.")]
    [InlineData("I have a dream that one day this nation will rise up.")]
    [InlineData("select the file from the list and press delete")]
    [InlineData("Let me know if you can make it. Thanks!")]
    [InlineData("42")]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("   \n  \n ")]
    [InlineData("{not json at all")]
    [InlineData("[just a note]")]
    [InlineData("<3 you")]
    [InlineData("a < b and c > d")]
    [InlineData("SELECT YOUR SEAT FROM THE LIST")]
    [InlineData("sudo make me a sandwich")]
    public void Ordinary_text_is_not_code(string text)
    {
        CodeDetector.Guess(text).ShouldBeNull();
    }

    [Fact]
    public void Only_the_start_of_a_very_long_text_is_read_and_the_work_stays_bounded()
    {
        var prose = string.Concat(Enumerable.Repeat("Just some ordinary words in a sentence. ", 50_000));

        CodeDetector.Guess(prose).ShouldBeNull();
        CodeDetector.Guess("def f(x):\n    return x\n" + new string('x', 2_000_000)).ShouldBe("Python");
    }

    [Fact]
    public void Json_that_is_nested_beyond_the_limit_is_not_called_json()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 500)) + string.Concat(Enumerable.Repeat("]", 500));

        CodeDetector.Guess(deep).ShouldNotBe("JSON");
    }

    [Fact]
    public void Warming_up_builds_the_patterns_ahead_of_time_without_changing_any_answer()
    {
        CodeDetector.Warm();
        CodeDetector.Warm();

        CodeDetector.Guess("def f(x):\n    return x\n").ShouldBe("Python");
        CodeDetector.Guess("just words").ShouldBeNull();
    }

    [Fact]
    public void Missing_text_is_a_bug_for_code()
    {
        Should.Throw<ArgumentNullException>(() => CodeDetector.Guess(null!));
    }
}
