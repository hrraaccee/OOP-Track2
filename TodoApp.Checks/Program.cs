using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var testDirectory = Path.Combine(Path.GetTempPath(), "TodoAppChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
var database = Path.Combine(testDirectory, "checks.db");
var connection = $"Data Source={database}";
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connection);
var passed = 0;
var taskId = 0;

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS {name}");
}

WebApplicationFactory<TodoApp.Program> CreateHost() => new WebApplicationFactory<TodoApp.Program>().WithWebHostBuilder(builder =>
{
    builder.UseEnvironment("Development");
    builder.ConfigureAppConfiguration((context, configuration) => configuration.AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }));
    builder.ConfigureLogging(logging => logging.ClearProviders());
});

HttpClient Client(WebApplicationFactory<TodoApp.Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    HandleCookies = true,
    BaseAddress = new Uri("https://localhost")
});

async Task<HttpResponseMessage> Submit(HttpClient client, string url, Dictionary<string, string> values)
{
    var page = await client.GetAsync(url);
    page.EnsureSuccessStatusCode();
    var html = await page.Content.ReadAsStringAsync();
    var fields = new Dictionary<string, string>();
    foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
    {
        var name = Regex.Match(input.Value, "\\bname=\"([^\"]*)\"").Groups[1].Value;
        if (name is "__RequestVerificationToken" or "_handler" or "Input.Confirm")
            fields[name] = WebUtility.HtmlDecode(Regex.Match(input.Value, "\\bvalue=\"([^\"]*)\"").Groups[1].Value);
    }
    foreach (var item in values) fields[item.Key] = item.Value;
    return await client.PostAsync(url, new FormUrlEncodedContent(fields));
}

Dictionary<string, string> Registration(string email) => new()
{
    ["Input.Email"] = email, ["Input.Password"] = "ExamplePass123", ["Input.ConfirmPassword"] = "ExamplePass123"
};

Dictionary<string, string> TaskValues(string title, string description = "Описание задачи") => new()
{
    ["Input.Title"] = title, ["Input.Description"] = description,
    ["Input.FinishDate"] = "2026-12-31", ["Input.IsCompleted"] = "false"
};

bool Redirect(HttpResponseMessage response) => (int)response.StatusCode is 302 or 303;

await using (var host = CreateHost())
{
    using var anonymous = Client(host);
    var response = await anonymous.GetAsync("/tasks/add");
    Check(Redirect(response) && response.Headers.Location!.ToString().Contains("Account/Login"), "Anonymous access redirects to login");
    response = await anonymous.PostAsync("/Account/Register", new FormUrlEncodedContent(Registration("blocked@example.test")));
    Check(response.StatusCode == HttpStatusCode.BadRequest, "Registration rejects missing antiforgery token");
    response = await Submit(anonymous, "/Account/Register", new() { ["Input.Email"] = "invalid", ["Input.Password"] = "a", ["Input.ConfirmPassword"] = "b" });
    Check(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("validation-message"), "Registration validates email and passwords");

    using var alice = Client(host);
    response = await Submit(alice, "/Account/Register", Registration("alice@example.test"));
    Check(Redirect(response), "Registration creates account and signs in");
    var page = WebUtility.HtmlDecode(await alice.GetStringAsync("/"));
    Check(page.Contains("Начните с первой задачи"), "New account has empty task list");
    response = await Submit(anonymous, "/Account/Register", Registration("alice@example.test"));
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("уже существует"), "Duplicate account rejected");
    response = await Submit(alice, "/tasks/add", TaskValues("", ""));
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Введите заголовок"), "Empty task rejected");
    response = await Submit(alice, "/tasks/add", TaskValues(new string('x', 121)));
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("120"), "Oversized title rejected");
    response = await Submit(alice, "/tasks/add", TaskValues("   "));
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Введите заголовок"), "Whitespace title rejected");
    var badDate = TaskValues("Некорректная дата");
    badDate["Input.FinishDate"] = "not-a-date";
    response = await Submit(alice, "/tasks/add", badDate);
    Check(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("validation-message"), "Invalid date rejected");
    response = await Submit(alice, "/tasks/add", TaskValues("Первая задача", "<script>alert(1)</script>"));
    Check(Redirect(response), "Task created");
    var rawPage = await alice.GetStringAsync("/");
    page = WebUtility.HtmlDecode(rawPage);
    taskId = int.Parse(Regex.Match(rawPage, "data-task-id=\"(\\d+)\"").Groups[1].Value);
    Check(page.Contains("Первая задача") && page.Contains("31.12.2026"), "Task fields displayed");
    Check(!rawPage.Contains("<script>alert(1)</script>") && rawPage.Contains("&lt;script&gt;"), "Task text is HTML encoded");

    using var bob = Client(host);
    response = await Submit(bob, "/Account/Register", Registration("bob@example.test"));
    Check(Redirect(response), "Second account registered");
    page = WebUtility.HtmlDecode(await bob.GetStringAsync("/"));
    Check(!page.Contains("Первая задача"), "Task list isolated between accounts");
    page = WebUtility.HtmlDecode(await bob.GetStringAsync($"/tasks/{taskId}"));
    Check(page.Contains("не найдена или недоступна"), "Another user cannot read task by ID");
    response = await Submit(bob, $"/tasks/{taskId}", TaskValues("Чужое изменение"));
    Check(!WebUtility.HtmlDecode(await alice.GetStringAsync("/")).Contains("Чужое изменение"), "Another user cannot edit task by ID");
    await Submit(bob, $"/tasks/{taskId}/delete", new() { ["_handler"] = "delete" });
    Check(WebUtility.HtmlDecode(await alice.GetStringAsync("/")).Contains("Первая задача"), "Another user cannot delete task by ID");

    var edit = TaskValues("Обновлённая задача", "Сохранено после изменения");
    edit["Input.IsCompleted"] = "true";
    edit["Input.FinishDate"] = "";
    response = await Submit(alice, $"/tasks/{taskId}", edit);
    Check(Redirect(response), "Task updated");
    page = WebUtility.HtmlDecode(await alice.GetStringAsync("/"));
    Check(page.Contains("Обновлённая задача") && page.Contains("Без срока") && page.Contains("Выполнено"), "Edited fields and completion stored");
    response = await alice.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
    Check(response.StatusCode == HttpStatusCode.BadRequest, "Logout rejects missing antiforgery token");
    var logoutHtml = await alice.GetStringAsync("/");
    var token = WebUtility.HtmlDecode(Regex.Match(logoutHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    response = await alice.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    Check(Redirect(response), "Logout succeeds with antiforgery token");
    Check(Redirect(await alice.GetAsync("/")), "Logout removes access to task list");
    response = await Submit(alice, "/Account/Login", new() { ["Input.Email"] = "alice@example.test", ["Input.Password"] = "WrongPass123" });
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Неверная почта или пароль"), "Incorrect password rejected");
    for (var index = 0; index < 4; index++)
        response = await Submit(bob, "/Account/Login", new() { ["Input.Email"] = "bob@example.test", ["Input.Password"] = "WrongPass123" });
    response = await Submit(bob, "/Account/Login", new() { ["Input.Email"] = "bob@example.test", ["Input.Password"] = "WrongPass123" });
    Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Слишком много попыток"), "Repeated failed login is locked out");
}

await using (var host = CreateHost())
{
    using var alice = Client(host);
    var response = await Submit(alice, "/Account/Login", new() { ["Input.Email"] = "alice@example.test", ["Input.Password"] = "ExamplePass123" });
    Check(Redirect(response), "Account persists after application restart");
    var page = WebUtility.HtmlDecode(await alice.GetStringAsync("/"));
    Check(page.Contains("Обновлённая задача"), "Task persists after application restart");
    response = await alice.GetAsync($"/tasks/{taskId}/delete");
    Check(WebUtility.HtmlDecode(await alice.GetStringAsync("/")).Contains("Обновлённая задача"), "Opening deletion confirmation does not delete data");
    response = await Submit(alice, $"/tasks/{taskId}/delete", new());
    Check(Redirect(response), "Confirmed deletion succeeds");
    Check(!WebUtility.HtmlDecode(await alice.GetStringAsync("/")).Contains("Обновлённая задача"), "Deleted task disappears from list");
    page = WebUtility.HtmlDecode(await alice.GetStringAsync($"/tasks/{taskId}"));
    Check(page.Contains("не найдена или недоступна"), "Deleted task cannot be reopened");
}

Console.WriteLine($"Completed: {passed} checks passed.");
