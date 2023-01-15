﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt.Documents;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class CompletionTests : IAsyncLifetime, IClassFixture<RoslynServicesFixture>
{
    private readonly RoslynServices services;
    private readonly CSharpReplPromptCallbacks promptCallbacks;

    public CompletionTests(RoslynServicesFixture fixture)
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        this.services = fixture.RoslynServices;
        promptCallbacks = new CSharpReplPromptCallbacks(console, services, new Configuration());
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Complete_GivenCode_ReturnsCompletions()
    {
        var completions = await this.services.CompleteAsync("Console.Writ", 12);
        var writelines = completions
            .Where(c => c.Item.DisplayText.StartsWith("Write"))
            .ToList();

        Assert.Equal("Write", writelines[0].Item.DisplayText);
        Assert.Equal("WriteLine", writelines[1].Item.DisplayText);

        var writeDescription = await writelines[0].GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Writes the text representation of the specified", writeDescription.Text);
        var writeLineDescription = await writelines[1].GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Writes the current line terminator to the standard output", writeLineDescription.Text);
    }

    [Fact]
    public async Task Complete_GivenLinq_ReturnsCompletions()
    {
        // LINQ tends to be a good canary for whether or not our reference / implementation assemblies are correct.
        var completions = await this.services.CompleteAsync("new[] { 1, 2, 3 }.Wher", 21);

        var whereCompletion = completions.SingleOrDefault(c => c.Item.DisplayText.StartsWith("Where"));

        Assert.NotNull(whereCompletion);
        Assert.Equal("Where", whereCompletion.Item.DisplayText);

        var whereDescription = await whereCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Filters a sequence of values based on a predicate", whereDescription.Text);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/4
    /// </summary>
    [Fact]
    public async Task Complete_SyntaxHighlight_CachesAreIsolated()
    {
        // type "c" which triggers completion at index 1, and is cached
        var completions = await this.services.CompleteAsync("c", 1);

        // next, type the number 1, which could collide with the previous cached value if the caches
        // aren't isolated, resulting in an exception
        var highlights = await this.services.SyntaxHighlightAsync("c1");

        Assert.NotEmpty(completions);
        Assert.NotEmpty(highlights);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/65
    /// </summary>
    [Fact]
    public async Task Complete_GetDescriptionForShorterCompletion()
    {
        var completions = await this.services.CompleteAsync("datetime", 8);
        var arrayCompletion = completions.SingleOrDefault(c => c.Item.DisplayText == "Array");
        Assert.NotNull(arrayCompletion);
        var arrayDescription = await arrayCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Provides methods for creating, manipulating, searching, and sorting arrays", arrayDescription.Text);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/92
    /// </summary>
    [Fact]
    public async Task Complete_GetDescriptionAfterDot()
    {
        var completions = await this.services.CompleteAsync("\"\".Where()", 3);
        var whereCompletion = completions.SingleOrDefault(c => c.Item.DisplayText == "Where");
        Assert.NotNull(whereCompletion);
        var whereDescription = await whereCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Filters a sequence of values based on a predicate", whereDescription.Text);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/215
    /// </summary>
    [Theory]
    [InlineData("fil", "file", "File", "FileAccess")]
    [InlineData("Fil", "File", "FileAccess", "FileAttributes")]
    [InlineData("con", "const", "ConcurrentExclusiveSchedulerPair", "Console")]
    [InlineData("Con", "ConcurrentExclusiveSchedulerPair", "Console", "ConsoleCancelEventArgs")]
    [InlineData("en", "enum", "Encoder")]
    [InlineData("enu", "enum", "Enum", "Enumerable")]
    [InlineData("Enu", "Enum", "Enumerable")]
    public async Task Complete_ItemsFilteringAndOrder(string text, params string[] expectedItems)
    {
        var caret = text.Length;
        var span = new TextSpan(0, text.Length);
        var completions = (await this.services.CompleteAsync(text, caret))
            .OrderByDescending(i => promptCallbacks.CreatePrettyPromptCompletionItem(i).GetCompletionItemPriority(text, caret, span));

        for (int i = 0; i < expectedItems.Length; i++)
        {
            Assert.Equal(expectedItems[i], completions.ElementAt(i).Item.DisplayText);
        }
    }
}