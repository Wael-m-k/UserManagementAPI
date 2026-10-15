using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Global error-handling middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unhandled exception: {ex}");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{ \"error\": \"Internal server error.\" }");
        }
    }
});

// Token-based authentication middleware
app.Use(async (context, next) =>
{
    // Look for the Authorization header
    if (!context.Request.Headers.TryGetValue("Authorization", out var token) ||
        token != "secret-token")
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized");
        return; // Block the request
    }

    // If token is valid, continue
    await next();
});


//custom logging middleware
app.Use(async (context, next) =>
{
    Console.WriteLine($"Incoming Request: {context.Request.Method} {context.Request.Path}");
    await next();
    Console.WriteLine($"Response Status: {context.Response.StatusCode}");
});


// In-memory store (thread-safe)
var users = new ConcurrentDictionary<int, User>();
var idCounter = 0;

// Seed user
var firstId = Interlocked.Increment(ref idCounter);
users[firstId] = new User { Id = firstId, Name = "Wael", Email = "wael@example.com" };


// GET all
app.MapGet("/users", () =>
{
    try
    {
        return Results.Ok(users.Values);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

// GET by id
app.MapGet("/users/{id:int}", (int id) =>
{
    try
    {
        if (!users.TryGetValue(id, out var user))
            return Results.NotFound($"User with ID {id} not found.");

        return Results.Ok(user);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

//test error handling
app.MapGet("/test-error", () =>
{
    throw new Exception("Simulated failure");
});


// POST (create)
app.MapPost("/users", async (HttpRequest req) =>
{
    try
    {
        var newUser = await req.ReadFromJsonAsync<User>();
        if (newUser is null)
            return Results.BadRequest("Invalid JSON payload.");

        if (string.IsNullOrWhiteSpace(newUser.Name))
            return Results.BadRequest("Name is required.");

        if (string.IsNullOrWhiteSpace(newUser.Email) || 
            !new EmailAddressAttribute().IsValid(newUser.Email))
            return Results.BadRequest("Valid email is required.");

        var id = Interlocked.Increment(ref idCounter);
        newUser.Id = id;

        if (!users.TryAdd(id, newUser))
            return Results.Conflict("Could not add user due to concurrency issue.");

        return Results.Created($"/users/{id}", newUser);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

// PUT (update)
app.MapPut("/users/{id:int}", async (int id, HttpRequest req) =>
{
    try
    {
        var update = await req.ReadFromJsonAsync<User>();
        if (update is null)
            return Results.BadRequest("Invalid JSON payload.");

        if (!users.TryGetValue(id, out var existing))
            return Results.NotFound($"User with ID {id} not found.");

        if (!string.IsNullOrWhiteSpace(update.Name))
            existing.Name = update.Name;

        if (!string.IsNullOrWhiteSpace(update.Email))
        {
            if (!new EmailAddressAttribute().IsValid(update.Email))
                return Results.BadRequest("Invalid email format.");
            existing.Email = update.Email;
        }

        if (update.Department is not null)
            existing.Department = update.Department;

        users[id] = existing;
        return Results.Ok(existing);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

// DELETE
app.MapDelete("/users/{id:int}", (int id) =>
{
    try
    {
        if (!users.TryRemove(id, out _))
            return Results.NotFound($"User with ID {id} not found.");

        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

app.Run();


// Models
public class User
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = default!;

    [Required, EmailAddress]
    public string Email { get; set; } = default!;

    public string? Department { get; set; }
}
