using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

// Development-time inventory only. Uses the compiler shipped in the SDK, never app dependencies.
string root = Path.GetFullPath(args[0]);
var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "desktop/DeskPet.Desktop"), "*.cs", SearchOption.AllDirectories))
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "desktop/DeskPet.Platform.Mac"), "*.cs", SearchOption.AllDirectories))
    .Where(f => !f.Replace('\\','/').Contains("/Tests/") && !f.Replace('\\','/').Contains("/obj/") && !f.Replace('\\','/').Contains("/bin/")).OrderBy(f => f))
{
    string source = File.ReadAllText(file);
    var seen = new HashSet<int>();
    foreach (var defines in new[] { Array.Empty<string>(), new[] { "PORTABLE" }, new[] { "UPDATE_WORKER" } })
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: defines));
        var syntax = tree.GetRoot();
        foreach (var literal in syntax.DescendantNodes().Where(n => n is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression) || n is InterpolatedStringExpressionSyntax))
        {
            if (literal.Ancestors().Any(n => n is InterpolatedStringExpressionSyntax)) continue;
            if (!seen.Add(literal.SpanStart)) continue;
            string value = literal is LiteralExpressionSyntax l ? l.Token.ValueText : literal.ToString();
            var ancestors = literal.Ancestors().ToArray();
            var member = ancestors.FirstOrDefault(n => n is MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax or FieldDeclarationSyntax);
            string name = member switch { MethodDeclarationSyntax m => m.Identifier.Text, ConstructorDeclarationSyntax c => c.Identifier.Text,
                PropertyDeclarationSyntax p => p.Identifier.Text, FieldDeclarationSyntax f => f.Declaration.Variables.First().Identifier.Text, _ => "topLevel" };
            var target = ancestors.FirstOrDefault(n => n is AssignmentExpressionSyntax or VariableDeclaratorSyntax or InvocationExpressionSyntax);
            var statement = ancestors.FirstOrDefault(n => n is StatementSyntax or MemberDeclarationSyntax);
            SyntaxNode concat=literal;
            while(concat.Parent is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression) || concat.Parent is ParenthesizedExpressionSyntax)concat=concat.Parent!;
            var parts=new List<object>();
            void Flatten(SyntaxNode node)
            {
                if(node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression) &&
                    binary.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(n=>n.IsKind(SyntaxKind.StringLiteralExpression)))
                {Flatten(binary.Left);Flatten(binary.Right);}
                else if(node is LiteralExpressionSyntax text && text.IsKind(SyntaxKind.StringLiteralExpression))parts.Add(new {text=text.Token.ValueText});
                else parts.Add(new {expression=node.ToString()});
            }
            if(concat is BinaryExpressionSyntax)Flatten(concat);
            Console.WriteLine(JsonSerializer.Serialize(new {
                file = Path.GetRelativePath(root, file).Replace('\\','/'), start = literal.SpanStart, length = literal.Span.Length,
                line = tree.GetLineSpan(literal.Span).StartLinePosition.Line + 1, value, raw = literal.ToString(), member = name,
                kind = literal.Kind().ToString(), parent = literal.Parent?.Kind().ToString(),
                ancestors = ancestors.Take(8).Select(n => n.Kind().ToString()), target = target?.ToString(),
                context = statement?.ToString(), constant = ancestors.Any(n => n is AttributeSyntax or ParameterSyntax or CaseSwitchLabelSyntax ||
                    n is FieldDeclarationSyntax fd && fd.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                    n is LocalDeclarationStatementSyntax ld && ld.Modifiers.Any(SyntaxKind.ConstKeyword)),
                concat = concat is BinaryExpressionSyntax ? new {start=concat.SpanStart,length=concat.Span.Length,raw=concat.ToString(),parts} : null
            }, options));
        }
    }
}
