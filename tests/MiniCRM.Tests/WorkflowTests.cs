using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MiniCRM.Models;
using PdfSharp.Pdf.IO;
using Xunit;

namespace MiniCRM.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly CrmFactory factory = new();

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/clients")]
    [InlineData("/deals")]
    [InlineData("/tasks")]
    [InlineData("/reports")]
    [InlineData("/admin")]
    public async Task Main_pages_render_and_supply_antiforgery_tokens(string path)
    {
        using var client = factory.CreateSession();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Follow_up_link_shows_a_form_and_saves_a_task_for_the_selected_deal()
    {
        using var client = factory.CreateSession();
        using var dealPage = await client.GetAsync("/deals?dealId=201");
        var dealHtml = await dealPage.Content.ReadAsStringAsync();
        var followUpLink = Regex.Match(dealHtml, "href=[\"'](/tasks\\?[^\"']*dealId=201)[\"']");
        Assert.True(followUpLink.Success, "The deal page must link to a follow-up form for this deal.");
        using var page = await client.GetAsync(WebUtility.HtmlDecode(followUpLink.Groups[1].Value));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Matches("action=[\"']/tasks/add[\"']", html);
        Assert.Contains("name=\"dueDate\"", html);
        Assert.Contains("name=\"priority\"", html);

        using var response = await factory.Post(client, "/tasks/add", new Dictionary<string, string>
        {
            ["dealId"] = "201", ["title"] = "New follow-up", ["dueDate"] = "2026-10-10", ["priority"] = "High", ["owner"] = "Anna Sales"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var task = Assert.Single(factory.Snapshot.Tasks, task => task.Title == "New follow-up");
        Assert.Equal(201, task.DealId);
        Assert.Equal(10, task.ClientId);
        Assert.Equal("Open", task.Status);
    }

    [Fact]
    public async Task Submitted_text_is_encoded_when_rendered_in_client_360()
    {
        using var client = factory.CreateSession();
        using var response = await factory.Post(client, "/clients/note", new Dictionary<string, string>
        {
            ["clientId"] = "10", ["note"] = "<script>alert('xss')</script> & follow-up"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var page = await client.GetAsync("/clients?clientId=10");
        var html = await page.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp; follow-up", html);
    }

    [Fact]
    public async Task Concurrent_notes_are_all_persisted_with_unique_ids()
    {
        using var client = factory.CreateSession();
        var token = await factory.Token(client);
        const int requests = 24;
        var initialNotes = factory.Snapshot.Clients.SelectMany(item => item.Notes).Select(note => note.Id).ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, requests).Select(index => factory.Post(client, "/clients/note", new Dictionary<string, string>
        {
            ["clientId"] = "10", ["note"] = $"Concurrent note {index}"
        }, token)));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var persisted = JsonSerializer.Deserialize<CrmData>(await File.ReadAllTextAsync(factory.DataPath))!;
        var added = persisted.Clients.Single(item => item.Id == 10).Notes.Where(note => note.Text.StartsWith("Concurrent note ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(requests, added.Length);
        var allNoteIds = persisted.Clients.SelectMany(item => item.Notes).Select(note => note.Id).ToArray();
        Assert.Equal(allNoteIds.Length, allNoteIds.Distinct().Count());
        Assert.All(added, note => Assert.DoesNotContain(note.Id, initialNotes));
        Assert.Equal(requests, factory.Snapshot.Clients.Single(item => item.Id == 10).Notes.Count(note => note.Text.StartsWith("Concurrent note ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Spreadsheet_export_is_valid_xml_and_preserves_unicode_and_xml_characters()
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        const string title = "Žalgiris & Łódź <sales> Пример";
        factory.Store.Change(data =>
        {
            data.Deals[0].Title = title;
            return true;
        });

        using var response = await client.GetAsync("/reports/export/xls?from=2020-01-01&to=2030-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("mini-crm-report.xml", response.Content.Headers.ContentDisposition?.FileName);
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(xml.Descendants().Select(element => element.Value), value => value.Contains(title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pdf_export_spans_multiple_pages_instead_of_truncating_a_long_report()
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        factory.Store.Change(data =>
        {
            data.Deals = Enumerable.Range(0, 100).Select(index => new Deal
            {
                Id = 1000 + index,
                ClientId = 10,
                Title = $"Žalgiris sale {index:D3}",
                Stage = "New",
                Value = index + 1,
                Deadline = "2026-10-31",
                Owner = "Anna Sales",
                Created = "2026-10-09",
                LastActivityDate = "2026-10-09"
            }).ToList();
            data.Tasks.Clear();
            return true;
        });

        using var response = await client.GetAsync("/reports/export/pdf?from=2026-10-01&to=2026-10-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var pdf = PdfReader.Open(bytes, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1, $"Expected multiple pages for 100 deals, received {pdf.PageCount}.");
    }

    [Fact]
    public async Task Dashboard_handles_a_last_activity_at_the_maximum_supported_date()
    {
        using var client = factory.CreateSession();
        factory.Store.Change(data =>
        {
            data.Deals.Single(deal => deal.Id == 201).LastActivityDate = "9999-12-31";
            return true;
        });

        using var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Open", "2026-10-08", "Overdue")]
    [InlineData("In progress", "2026-10-08", "Overdue")]
    [InlineData("Done", "2026-10-08", "Done")]
    [InlineData("Overdue", "2026-10-10", "Open")]
    public async Task Client_360_and_task_list_show_the_same_derived_task_status(string storedStatus, string dueDate, string displayedStatus)
    {
        using var client = factory.CreateSession();
        factory.Store.Change(data =>
        {
            var task = data.Tasks.Single(task => task.Id == 301);
            task.Title = "Date-sensitive follow-up";
            task.DueDate = dueDate;
            task.Status = storedStatus;
            data.Tasks = [task];
            return true;
        });
        foreach (var path in new[] { "/tasks", "/clients?clientId=10" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Date-sensitive follow-up", html);
            Assert.Matches($"<span class='badge [^']+'>{displayedStatus}</span>", html);
        }
        Assert.Equal(storedStatus, factory.Snapshot.Tasks[0].Status);
    }

    public void Dispose() => factory.Dispose();
}
