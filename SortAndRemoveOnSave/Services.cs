﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace SortAndRemoveOnSave
{
    partial class CSharpRemoveUnnecessaryImportsService
    {
        private class Rewriter : CSharpSyntaxRewriter
        {
            private readonly ISet<UsingDirectiveSyntax> _unnecessaryUsingsDoNotAccessDirectly;
            private readonly CancellationToken _cancellationToken;

            public Rewriter(ISet<UsingDirectiveSyntax> unnecessaryUsings, CancellationToken cancellationToken)
                : base(visitIntoStructuredTrivia: true)
            {
                _unnecessaryUsingsDoNotAccessDirectly = unnecessaryUsings;
                _cancellationToken = cancellationToken;
            }

            public override SyntaxNode DefaultVisit(SyntaxNode node)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                return base.DefaultVisit(node);
            }

            private void ProcessUsings(
                SyntaxList<UsingDirectiveSyntax> usings,
                ISet<UsingDirectiveSyntax> usingsToRemove,
                out SyntaxList<UsingDirectiveSyntax> finalUsings,
                out SyntaxTriviaList finalTrivia)
            {
                var currentUsings = new List<UsingDirectiveSyntax>(usings);

                finalTrivia = default(SyntaxTriviaList);
                for (int i = 0; i < usings.Count; i++)
                {
                    if (usingsToRemove.Contains(usings[i]))
                    {
                        var currentUsing = currentUsings[i];
                        currentUsings[i] = null;

                        var leadingTrivia = currentUsing.GetLeadingTrivia();
                        if (leadingTrivia.Any(t => t.Kind() != SyntaxKind.EndOfLineTrivia && t.Kind() != SyntaxKind.WhitespaceTrivia))
                        {
                            // This using had trivia we want to preserve.  If we're the last
                            // directive, then copy this trivia out so that our caller can place
                            // it on the next token.  If there is any directive following us,
                            // then place it on that.
                            if (i < usings.Count - 1)
                            {
                                currentUsings[i + 1] = currentUsings[i + 1].WithPrependedLeadingTrivia(leadingTrivia);
                            }
                            else
                            {
                                finalTrivia = leadingTrivia;
                            }
                        }
                    }
                }

                finalUsings = currentUsings.Where(u => u != null).ToSyntaxList();
            }

            private ISet<UsingDirectiveSyntax> GetUsingsToRemove(
                SyntaxList<UsingDirectiveSyntax> oldUsings,
                SyntaxList<UsingDirectiveSyntax> newUsings)
            {
                var result = new HashSet<UsingDirectiveSyntax>();
                for (int i = 0; i < oldUsings.Count; i++)
                {
                    if (_unnecessaryUsingsDoNotAccessDirectly.Contains(oldUsings[i]))
                    {
                        result.Add(newUsings[i]);
                    }
                }

                return result;
            }

            public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
            {
                var compilationUnit = (CompilationUnitSyntax)base.VisitCompilationUnit(node);

                var usingsToRemove = GetUsingsToRemove(node.Usings, compilationUnit.Usings);
                if (usingsToRemove.Count == 0)
                {
                    return compilationUnit;
                }

                SyntaxList<UsingDirectiveSyntax> finalUsings;
                SyntaxTriviaList finalTrivia;
                ProcessUsings(compilationUnit.Usings, usingsToRemove, out finalUsings, out finalTrivia);
                finalUsings = RemoveEmptyLines(finalUsings);

                // If there was any left over trivia, then attach it to the next token that
                // follows the usings.
                if (finalTrivia.Count > 0)
                {
                    var nextToken = compilationUnit.Usings.Last().GetLastToken().GetNextToken();
                    compilationUnit = compilationUnit.ReplaceToken(nextToken, nextToken.WithPrependedLeadingTrivia(finalTrivia));
                }

                var resultCompilationUnit = compilationUnit.WithUsings(finalUsings);
                if (finalUsings.Count == 0 &&
                    resultCompilationUnit.Externs.Count == 0 &&
                    resultCompilationUnit.Members.Count >= 1)
                {
                    // We've removed all the usings and now the first thing in the namespace is a
                    // type.  In this case, remove any newlines preceding the type.
                    var firstToken = resultCompilationUnit.GetFirstToken();
                    var newFirstToken = StripNewLines(firstToken);
                    resultCompilationUnit = resultCompilationUnit.ReplaceToken(firstToken, newFirstToken);
                }

                return resultCompilationUnit;
            }

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                var namespaceDeclaration = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);
                var usingsToRemove = GetUsingsToRemove(node.Usings, namespaceDeclaration.Usings);
                if (usingsToRemove.Count == 0)
                {
                    return namespaceDeclaration;
                }

                SyntaxList<UsingDirectiveSyntax> finalUsings;
                SyntaxTriviaList finalTrivia;
                ProcessUsings(namespaceDeclaration.Usings, usingsToRemove, out finalUsings, out finalTrivia);
                finalUsings = RemoveEmptyLines(finalUsings);

                // If there was any left over trivia, then attach it to the next token that
                // follows the usings.
                if (finalTrivia.Count > 0)
                {
                    var nextToken = namespaceDeclaration.Usings.Last().GetLastToken().GetNextToken();
                    namespaceDeclaration = namespaceDeclaration.ReplaceToken(nextToken, nextToken.WithPrependedLeadingTrivia(finalTrivia));
                }

                var resultNamespace = namespaceDeclaration.WithUsings(finalUsings);
                if (finalUsings.Count == 0 &&
                    resultNamespace.Externs.Count == 0 &&
                    resultNamespace.Members.Count >= 1)
                {
                    // We've removed all the usings and now the first thing in the namespace is a
                    // type.  In this case, remove any newlines preceding the type.
                    var firstToken = resultNamespace.Members.First().GetFirstToken();
                    var newFirstToken = StripNewLines(firstToken);
                    resultNamespace = resultNamespace.ReplaceToken(firstToken, newFirstToken);
                }

                return resultNamespace;
            }

            private SyntaxList<UsingDirectiveSyntax> RemoveEmptyLines(SyntaxList<UsingDirectiveSyntax> finalUsings)
            {
                return SyntaxFactory.List(finalUsings.Select(u =>
                {
                    var trivia = u.GetLeadingTrivia();
                    if (trivia.Count > 0 && trivia.All((arg) => arg.Kind() == SyntaxKind.WhitespaceTrivia))
                    {
                        return u.WithLeadingTrivia(trivia.Where(t => t.Kind() != SyntaxKind.EndOfLineTrivia));
                    }
                    return u;
                }));
            }

            private static SyntaxToken StripNewLines(SyntaxToken firstToken)
            {
                return firstToken.WithLeadingTrivia(firstToken.LeadingTrivia.SkipWhile(t => t.Kind() == SyntaxKind.EndOfLineTrivia));
            }
        }



        public static IEnumerable<SyntaxNode> GetUnnecessaryImports(SemanticModel semanticModel, SyntaxNode root, CancellationToken cancellationToken)
        {
            var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);
            if (!diagnostics.Any())
            {
                return null;
            }

            var unnecessaryImports = new HashSet<UsingDirectiveSyntax>();

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Id == "CS8019")
                {
                    var node = root.FindNode(diagnostic.Location.SourceSpan) as UsingDirectiveSyntax;

                    if (node != null)
                    {
                        unnecessaryImports.Add(node);
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested || !unnecessaryImports.Any())
            {
                return null;
            }

            return unnecessaryImports;
        }

        public Document RemoveUnnecessaryImports(Document document, SemanticModel model, SyntaxNode root, CancellationToken cancellationToken)
        {
            var unnecessaryImports = GetUnnecessaryImports(model, root, cancellationToken) as ISet<UsingDirectiveSyntax>;
            if (unnecessaryImports == null)
            {
                return document;
            }

            var oldRoot = (CompilationUnitSyntax)root;
            if (unnecessaryImports.Any(import => oldRoot.OverlapsHiddenPosition(cancellationToken)))
            {
                return document;
            }

            var newRoot = (CompilationUnitSyntax)new Rewriter(unnecessaryImports, cancellationToken).Visit(oldRoot);
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return document.WithSyntaxRoot(FormatResult(document, newRoot, cancellationToken));
        }

        private SyntaxNode FormatResult(Document document, CompilationUnitSyntax newRoot, CancellationToken cancellationToken)
        {
            var spans = new List<TextSpan>();
            AddFormattingSpans(newRoot, spans, cancellationToken);
            return Formatter.Format(newRoot, spans, document.Project.Solution.Workspace, document.Project.Solution.Workspace.Options, cancellationToken: cancellationToken);
        }

        private void AddFormattingSpans(
            CompilationUnitSyntax compilationUnit,
            List<TextSpan> spans,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spans.Add(TextSpan.FromBounds(0, GetEndPosition(compilationUnit, compilationUnit.Members)));

            foreach (var @namespace in compilationUnit.Members.OfType<NamespaceDeclarationSyntax>())
            {
                AddFormattingSpans(@namespace, spans, cancellationToken);
            }
        }

        private void AddFormattingSpans(
            NamespaceDeclarationSyntax namespaceMember,
            List<TextSpan> spans,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spans.Add(TextSpan.FromBounds(namespaceMember.SpanStart, GetEndPosition(namespaceMember, namespaceMember.Members)));

            foreach (var @namespace in namespaceMember.Members.OfType<NamespaceDeclarationSyntax>())
            {
                AddFormattingSpans(@namespace, spans, cancellationToken);
            }
        }

        private int GetEndPosition(SyntaxNode container, SyntaxList<MemberDeclarationSyntax> list)
        {
            return list.Count > 0 ? list[0].SpanStart : container.Span.End;
        }
    }

    public static class Extensions
    {


        public static T WithPrependedLeadingTrivia<T>(
            this T node,
            params SyntaxTrivia[] trivia) where T : SyntaxNode
        {
            if (trivia.Length == 0)
            {
                return node;
            }

            return node.WithPrependedLeadingTrivia((IEnumerable<SyntaxTrivia>)trivia);
        }

        public static T WithPrependedLeadingTrivia<T>(
            this T node,
            SyntaxTriviaList trivia) where T : SyntaxNode
        {
            if (trivia.Count == 0)
            {
                return node;
            }

            return node.WithLeadingTrivia(trivia.Concat(node.GetLeadingTrivia()));
        }

        public static T WithPrependedLeadingTrivia<T>(
            this T node,
            IEnumerable<SyntaxTrivia> trivia) where T : SyntaxNode
        {
            return node.WithPrependedLeadingTrivia(trivia.ToSyntaxTriviaList());
        }

        public static SyntaxToken WithPrependedLeadingTrivia(
            this SyntaxToken token,
            params SyntaxTrivia[] trivia)
        {
            if (trivia.Length == 0)
            {
                return token;
            }

            return token.WithPrependedLeadingTrivia((IEnumerable<SyntaxTrivia>)trivia);
        }

        public static SyntaxToken WithPrependedLeadingTrivia(
            this SyntaxToken token,
            SyntaxTriviaList trivia)
        {
            if (trivia.Count == 0)
            {
                return token;
            }

            return token.WithLeadingTrivia(trivia.Concat(token.LeadingTrivia));
        }

        public static SyntaxToken WithPrependedLeadingTrivia(
            this SyntaxToken token,
            IEnumerable<SyntaxTrivia> trivia)
        {
            return token.WithPrependedLeadingTrivia(trivia.ToSyntaxTriviaList());
        }

        public static SyntaxToken WithAppendedTrailingTrivia(
            this SyntaxToken token,
            IEnumerable<SyntaxTrivia> trivia)
        {
            return token.WithTrailingTrivia(token.TrailingTrivia.Concat(trivia));
        }


        public static SyntaxList<T> ToSyntaxList<T>(this IEnumerable<T> sequence) where T : SyntaxNode
        {
            return SyntaxFactory.List(sequence);
        }

        public static bool OverlapsHiddenPosition(
            this SourceText text, TextSpan span, Func<int, CancellationToken, bool> isPositionHidden, CancellationToken cancellationToken)
        {
            var result = TryOverlapsHiddenPosition(text, span, isPositionHidden, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        /// <summary>
        /// Same as OverlapsHiddenPosition but doesn't throw on cancellation.  Instead, returns false
        /// in that case.
        /// </summary>
        public static bool TryOverlapsHiddenPosition(
            this SourceText text, TextSpan span, Func<int, CancellationToken, bool> isPositionHidden,
            CancellationToken cancellationToken)
        {
            var startLineNumber = text.Lines.IndexOf(span.Start);
            var endLineNumber = text.Lines.IndexOf(span.End);

            // NOTE(cyrusn): It's safe to examine the start of a line because you can't have a line
            // with both a pp directive and code on it.  so, for example, if a node crosses a region
            // then it must be the case that the start of some line from the start of the node to
            // the end is hidden.  i.e.:
#if false
            '           class C
            '           {
            '#line hidden
            '           }
            '#line default
#endif
            // The start of the line with the } on it is hidden, and thus the node overlaps a hidden
            // region.
            for (var lineNumber = startLineNumber; lineNumber <= endLineNumber; lineNumber++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                var linePosition = text.Lines[lineNumber].Start;
                var isHidden = isPositionHidden(linePosition, cancellationToken);
                if (isHidden)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool OverlapsHiddenPosition(this SyntaxNode node, CancellationToken cancellationToken)
        {
            return node.OverlapsHiddenPosition(node.Span, cancellationToken);
        }

        public static bool OverlapsHiddenPosition(this SyntaxNode node, TextSpan span, CancellationToken cancellationToken)
        {
            return node.SyntaxTree.OverlapsHiddenPosition(span, cancellationToken);
        }

        public static bool OverlapsHiddenPosition(this SyntaxNode declaration, SyntaxNode startNode, SyntaxNode endNode, CancellationToken cancellationToken)
        {
            var start = startNode.Span.End;
            var end = endNode.SpanStart;

            var textSpan = TextSpan.FromBounds(start, end);
            return declaration.OverlapsHiddenPosition(textSpan, cancellationToken);
        }

        public static bool OverlapsHiddenPosition(this SyntaxTree tree, TextSpan span, CancellationToken cancellationToken)
        {
            if (tree == null)
            {
                return false;
            }

            var text = tree.GetText(cancellationToken);

            return text.OverlapsHiddenPosition(span, (position, cancellationToken2) =>
                {
                    // implements the ASP.Net IsHidden rule
                    var lineVisibility = tree.GetLineVisibility(position, cancellationToken2);
                    return lineVisibility == LineVisibility.Hidden || lineVisibility == LineVisibility.BeforeFirstLineDirective;
                },
                cancellationToken);
        }

        internal static void ApplyDocumentChanges(this Workspace workspace, Document newDocument, CancellationToken cancellationToken)
        {
            var oldSolution = workspace.CurrentSolution;
            var oldDocument = oldSolution.GetDocument(newDocument.Id);
            var changes = newDocument.GetTextChangesAsync(oldDocument, cancellationToken).WaitAndGetResult(cancellationToken);
            var newSolution = oldSolution.UpdateDocument(newDocument.Id, changes, cancellationToken);
            workspace.TryApplyChanges(newSolution);
        }

        internal static Solution UpdateDocument(this Solution solution, DocumentId id, IEnumerable<TextChange> textChanges, CancellationToken cancellationToken)
        {
            var oldDocument = solution.GetDocument(id);
            var oldText = oldDocument.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);
            var newText = oldText.WithChanges(textChanges);
            return solution.WithDocumentText(id, newText, PreservationMode.PreserveIdentity);
        }

        public static T WaitAndGetResult<T>(this Task<T> task, CancellationToken cancellationToken)
        {
#if false  // eventually this will go live for check-in
#if DEBUG
            if (Microsoft.CodeAnalysis.Workspace.PrimaryWorkspace != null &&  // only care if we are in a UI situation.. this keeps normal unit tests from failing                                
            Thread.CurrentThread.IsThreadPoolThread)
            {
            // This check is meant to catch improper waits on background threads when integration tests are run.
            System.Diagnostics.Debug.Fail("WaitAndGetResult called from thread pool thread.");
            }
#endif
#endif
            task.Wait(cancellationToken);
            return task.Result;
        }

    }
}
