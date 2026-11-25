using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class AuditLogsController : Controller
{
    private readonly DataContext _db;

    public AuditLogsController(DataContext db)
    {
        _db = db;
    }

    [HttpGet]
    [HttpGet("/Admin/AuditLogs")]
    public async Task<IActionResult> Index(string? q = null, string? actionFilter = null, CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => (x.ActorEmail != null && x.ActorEmail.Contains(term))
                                  || (x.Detail != null && x.Detail.Contains(term))
                                  || (x.EntityId != null && x.EntityId.Contains(term))
                                  || x.EntityType.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Where(x => x.Action == actionFilter.Trim());
        }

        var distinctActions = await _db.AuditLogs
            .AsNoTracking()
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var logs = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(150)
            .ToListAsync(ct);

        ViewBag.Query = q ?? "";
        ViewBag.ActionFilter = actionFilter ?? "";
        ViewBag.DistinctActions = distinctActions;

        return View(logs);
    }
}
