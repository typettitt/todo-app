using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Data;

/// <summary>
/// Applies pending EF Core migrations at startup. We never call
/// <c>EnsureCreated</c> — only real migrations — because <c>EnsureCreated</c>
/// bypasses the migration history table and silently desyncs the schema from
/// <c>0001_Init</c> onward.
/// </summary>
public static class DbInitializer
{
    private const string DemoEmail = "demo@example.com";
    private const string DemoPassword = "Demo123!";
    private const int DemoSeedTodoCount = 500;
    private const int DemoDueDateWindowDays = 365;

    public static async Task MigrateAsync(
        MaintenanceDbContext db,
        IHostEnvironment env,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(logger);

        logger.ApplyingMigrations(env.EnvironmentName);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.MigrationsApplied();
    }

    public static async Task SeedAsync(
        MaintenanceDbContext db,
        IHostEnvironment env,
        IClock clock,
        IPasswordHasher<User> hasher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(hasher);

        if (!env.IsDevelopment())
        {
            return;
        }

        var now = clock.Now;
        var today = DateOnly.FromDateTime(now.Date);
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == DemoEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = DemoEmail,
                Role = Role.Basic,
                CreatedAt = now,
                UpdatedAt = now,
            };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            db.Users.Add(user);
        }

        var existingTodos = await db.Todos
            .Where(t => t.UserId == user.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var shouldReseedTodos = ShouldReseedDemoTodos(existingTodos, today);

        if (!shouldReseedTodos)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        db.Todos.RemoveRange(existingTodos);
        db.Todos.AddRange(DemoTodos(user.Id, now, today));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<Todo> DemoTodos(Guid userId, DateTimeOffset now, DateOnly today)
    {
        for (var index = 0; index < DemoSeedTodoCount; index++)
        {
            var template = DemoTodoTemplates[index % DemoTodoTemplates.Length];
            var cycle = index / DemoTodoTemplates.Length;
            var context = DemoTitleContexts[(index + cycle) % DemoTitleContexts.Length];
            var contextTag = DemoContextTags[(index + cycle) % DemoContextTags.Length];
            var title = cycle == 0 ? template.Title : $"{template.Title} ({context})";
            var description = cycle == 0
                ? template.Description
                : $"{template.Description} Track this under {context.ToLowerInvariant()}.";

            var createdAt = now
                .AddDays(-DemoDueDateWindowDays + (index * 29 % (DemoDueDateWindowDays - 4)))
                .AddHours(-(index * 3 % 22))
                .AddMinutes(index * 11 % 49);
            var updatedAt = createdAt
                .AddDays(index * 5 % 18)
                .AddHours(index * 7 % 11);

            if (updatedAt > now.AddMinutes(-30))
            {
                updatedAt = now.AddMinutes(-30 - (index % 360));
            }

            var dueDate = DemoDueDate(index, today);
            var completedAt = ShouldCompleteDemoTodo(index, dueDate, today)
                ? updatedAt.AddHours(1 + (index % 8))
                : (DateTimeOffset?)null;

            if (completedAt > now.AddMinutes(-10))
            {
                completedAt = now.AddMinutes(-10 - (index % 240));
            }

            var priority = index % 9 == 0
                ? Priority.High
                : index % 4 == 0
                    ? Priority.Medium
                    : template.Priority;
            var tags = template.Tags
                .Append(contextTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            yield return DemoTodo(
                userId,
                title,
                description,
                priority,
                dueDate,
                tags,
                createdAt,
                updatedAt,
                completedAt);
        }
    }

    private static bool ShouldReseedDemoTodos(List<Todo> existingTodos, DateOnly today)
    {
        if (existingTodos.Count == 0
            || existingTodos.Count < DemoSeedTodoCount
            || existingTodos.Any(t => t.Title.EndsWith("demo todo", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var dueDates = existingTodos
            .Select(t => t.DueDate)
            .OfType<DateOnly>()
            .ToArray();

        return dueDates.Length == 0
            || !dueDates.Any(d => d <= today.AddDays(-180))
            || !dueDates.Any(d => d >= today.AddDays(180))
            || !dueDates.Contains(today.AddDays(-DemoDueDateWindowDays))
            || !dueDates.Contains(today.AddDays(DemoDueDateWindowDays));
    }

    private static DateOnly? DemoDueDate(int index, DateOnly today)
    {
        if (index % 23 == 0)
        {
            return null;
        }

        if (index == 1)
        {
            return today;
        }

        if (index == 2)
        {
            return today.AddDays(-DemoDueDateWindowDays);
        }

        if (index == 3)
        {
            return today.AddDays(DemoDueDateWindowDays);
        }

        var offset = (index * 37 % (DemoDueDateWindowDays * 2 + 1)) - DemoDueDateWindowDays;
        return today.AddDays(offset);
    }

    private static bool ShouldCompleteDemoTodo(int index, DateOnly? dueDate, DateOnly today)
    {
        if (dueDate is null)
        {
            return index % 29 == 0;
        }

        if (dueDate <= today)
        {
            return index % 4 == 0 || index % 11 == 0;
        }

        return index % 31 == 0;
    }

    private static readonly DemoTodoTemplate[] DemoTodoTemplates =
    [
        new("Renew vehicle registration", "Upload the insurance card, verify emissions status, and pay the county fee before the grace period ends.", Priority.High, ["personal", "car"]),
        new("Schedule annual physical", "Book the morning appointment and ask for fasting lab instructions.", Priority.Medium, ["health", "calendar"]),
        new("Submit quarterly tax estimate", "Review contractor income, confirm the safe-harbor amount, and submit the payment online.", Priority.High, ["finance", "taxes"]),
        new("Order HVAC filters", "Check the hallway unit size before ordering the replacement pack.", Priority.Low, ["home", "maintenance"]),
        new("Book dentist cleaning", "Find an early slot and update the calendar invite once confirmed.", Priority.Medium, ["health", "calendar"]),
        new("Review mortgage refinance documents", "Compare closing costs against the current rate before responding.", Priority.High, ["finance", "home"]),
        new("Replace smoke detector batteries", "Use the batteries in the utility drawer and test each room after replacement.", Priority.Medium, ["home", "safety"]),
        new("Plan grocery pickup", "Add breakfast items, lunches, and coffee before the pickup window closes.", Priority.Low, ["errands", "food"]),
        new("Send birthday gift to Maya", "Choose a book, add a short note, and ship directly to her apartment.", Priority.Medium, ["family", "errands"]),
        new("Reconcile monthly budget", "Categorize card transactions and update the savings transfer target.", Priority.Medium, ["finance", "budget"]),
        new("Cancel unused design tool trial", "Cancel before the card is charged and export any saved references first.", Priority.High, ["subscriptions", "admin"]),
        new("Prep Monday standup notes", "Summarize shipped work, blockers, and the next three priorities.", Priority.Medium, ["work", "planning"]),
        new("Upload insurance receipts", "Scan the urgent care and pharmacy receipts into the claims portal.", Priority.Medium, ["health", "paperwork"]),
        new("Schedule oil change", "Use the dealership coupon and ask them to inspect the tire rotation schedule.", Priority.Low, ["car", "errands"]),
        new("Follow up on contractor quote", "Ask for itemized labor, material allowances, and an earliest start date.", Priority.Medium, ["home", "repair"]),
        new("Finish expense report", "Attach hotel, rideshare, and meal receipts before the finance cutoff.", Priority.High, ["work", "finance"]),
        new("Set up auto-pay for utilities", "Enable electric and water auto-pay, then record confirmation numbers.", Priority.Low, ["home", "finance"]),
        new("Pick up dry cleaning", "Grab the blue suit and check for the missing collar stay.", Priority.Low, ["errands", "clothes"]),
        new("Compare internet plans", "Check fiber availability and cancel the legacy modem rental if switching.", Priority.Low, ["home", "subscriptions"]),
        new("RSVP to school fundraiser", "Confirm headcount and choose the vegetarian dinner option.", Priority.Medium, ["family", "calendar"]),
        new("Archive tax documents", "Move W-2s, 1099s, mortgage interest, and donation receipts into the shared folder.", Priority.Low, ["finance", "records"]),
        new("Check laundry room leak", "Run the washer, inspect the supply line, and photograph any moisture under the baseboard.", Priority.High, ["home", "repair"]),
        new("Drop off donation boxes", "Take garage boxes to the donation center and keep the receipt.", Priority.Low, ["errands", "home"]),
        new("Call bank about wire limit", "Ask whether the higher limit can be approved before Friday.", Priority.High, ["finance", "admin"]),
        new("Renew passport", "Confirm the photo requirements, complete the renewal form, and mail the packet.", Priority.High, ["travel", "documents"]),
        new("Update emergency contacts", "Review names, phone numbers, and access notes in the shared household file.", Priority.Medium, ["family", "records"]),
        new("Schedule eye exam", "Book an appointment before the insurance benefit resets.", Priority.Medium, ["health", "calendar"]),
        new("Clean out email subscriptions", "Unsubscribe from inactive newsletters and archive old receipts.", Priority.Low, ["admin", "inbox"]),
        new("Plan weekend meals", "Pick three dinners, check pantry staples, and add missing ingredients.", Priority.Low, ["food", "planning"]),
        new("Review credit card benefits", "Check expiring credits, travel insurance details, and annual fee timing.", Priority.Low, ["finance", "admin"]),
        new("Prepare presentation draft", "Turn the rough outline into a five-slide narrative and flag missing data.", Priority.High, ["work", "writing"]),
        new("Confirm childcare schedule", "Verify pickup coverage and share the updated plan with the family calendar.", Priority.High, ["family", "calendar"]),
        new("File warranty paperwork", "Register the appliance, upload the receipt, and save the confirmation email.", Priority.Low, ["home", "records"]),
        new("Pay property tax bill", "Confirm the parcel number and submit payment before the discount deadline.", Priority.High, ["finance", "home"]),
        new("Organize pantry shelf", "Move duplicates forward and add low-stock staples to the grocery list.", Priority.Low, ["home", "food"]),
        new("Schedule haircut", "Find a lunch-hour appointment and add travel time to the calendar.", Priority.Low, ["personal", "calendar"]),
        new("Review investment allocation", "Compare the target allocation against current balances and note rebalancing needs.", Priority.Medium, ["finance", "planning"]),
        new("Prepare client follow-up", "Send the recap, owners, and target dates from the last meeting.", Priority.High, ["work", "email"]),
        new("Renew library books", "Check due dates and renew anything that cannot be returned this week.", Priority.Low, ["personal", "errands"]),
        new("Order printer ink", "Confirm cartridge number and add paper if the office shelf is low.", Priority.Low, ["office", "supplies"]),
        new("Create packing checklist", "List clothes, chargers, documents, and day-of reminders.", Priority.Medium, ["travel", "planning"]),
        new("Update resume bullets", "Add recent project metrics and tighten the summary section.", Priority.Medium, ["career", "writing"]),
        new("Schedule furnace inspection", "Book the seasonal service appointment and ask about filter subscription options.", Priority.Medium, ["home", "maintenance"]),
        new("Check flexible spending balance", "Review eligible receipts and submit any outstanding reimbursements.", Priority.Medium, ["health", "finance"]),
        new("Plan quarterly goals", "Choose three outcomes, write success criteria, and schedule review checkpoints.", Priority.High, ["work", "planning"]),
        new("Return online order", "Print the label, repack the item, and drop it before the return window closes.", Priority.Medium, ["errands", "shopping"]),
        new("Verify backup drive", "Run a manual backup and confirm the last restore point is readable.", Priority.Medium, ["tech", "records"]),
        new("Update household inventory", "Photograph new purchases and add serial numbers to the insurance folder.", Priority.Low, ["home", "records"]),
        new("Book airport parking", "Compare off-site rates and reserve a covered spot near the shuttle.", Priority.Low, ["travel", "errands"]),
        new("Send thank-you notes", "Write short notes and mail them before the weekend.", Priority.Low, ["family", "writing"]),
    ];

    private static readonly string[] DemoTitleContexts =
    [
        "May week 1",
        "May week 2",
        "May week 3",
        "June planning",
        "summer prep",
        "home reset",
        "office week",
        "budget cycle",
        "school schedule",
        "travel prep",
        "renewals",
        "follow-up batch",
    ];

    private static readonly string[] DemoContextTags =
    [
        "may",
        "june",
        "summer",
        "home",
        "office",
        "budget",
        "school",
        "travel",
        "renewal",
        "follow-up",
    ];

    private static Todo DemoTodo(
        Guid userId,
        string title,
        string description,
        Priority priority,
        DateOnly? dueDate,
        string[] tags,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Description = description,
            DueDate = dueDate,
            Priority = priority,
            IsCompleted = completedAt is not null,
            CompletedAt = completedAt,
            Tags = tags.ToArray(),
            RowVersion = 1,
            CreatedAt = createdAt,
            UpdatedAt = completedAt ?? updatedAt,
        };

    private sealed record DemoTodoTemplate(string Title, string Description, Priority Priority, string[] Tags);
}
