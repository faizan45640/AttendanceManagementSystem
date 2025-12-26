using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class BatchController : Controller
    {
        private readonly ApplicationDbContext _context;
        public BatchController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        private object GetModelStateErrors()
        {
            return ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                );
        }

        public IActionResult Index()
        {
            return View();
        }


        [HttpGet]

        public async Task<IActionResult> Batches(BatchFilterViewModel filter)
        {
            filter.Page = filter.Page < 1 ? 1 : filter.Page;
            filter.PageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
            filter.PageSize = Math.Clamp(filter.PageSize, 10, 100);

            var query = _context.Batches
                .AsNoTracking()
                .Select(b => new BatchListItemViewModel
                {
                    BatchId = b.BatchId,
                    BatchName = b.BatchName,
                    Year = b.Year,
                    IsActive = b.IsActive,
                    StudentCount = b.Students.Count()
                })
                .AsQueryable();

            filter.TotalCount = await query.CountAsync();
            if (filter.TotalPages > 0 && filter.Page > filter.TotalPages)
            {
                filter.Page = filter.TotalPages;
            }

            filter.Batches = await query
                .OrderByDescending(b => b.Year)
                .ThenBy(b => b.BatchName)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return View(filter);
        }


        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult AddBatch()
        {
            var model = new AddBatchViewModel
            {
                Year = DateTime.Now.Year,
                IsActive = true
            };
            return View(model);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBatch(AddBatchViewModel model)
        {
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                return View(model);
            }

            // Check if batch name already exists for the same year
            var existingBatch = await _context.Batches
                .FirstOrDefaultAsync(b => b.BatchName == model.BatchName && b.Year == model.Year);

            if (existingBatch != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A batch with this name already exists for the selected year." });
                }

                ModelState.AddModelError(nameof(AddBatchViewModel.BatchName), "A batch with this name already exists for the selected year.");
                return View(model);
            }

            var batch = new Batch
            {
                BatchName = model.BatchName,
                Year = model.Year,
                IsActive = model.IsActive
            };

            _context.Batches.Add(batch);
            await _context.SaveChangesAsync();

            var successMessage = $"Batch '{model.BatchName}' has been added successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            TempData["Success"] = successMessage;
            return RedirectToAction(nameof(Batches));
        }


        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> EditBatch(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var batch = await _context.Batches.FindAsync(id);
            if (batch == null)
            {
                return NotFound();
            }

            var model = new AddBatchViewModel
            {
                BatchName = batch.BatchName ?? "N/A",
                Year = batch.Year ?? DateTime.Now.Year,
                IsActive = batch.IsActive
            };

            ViewBag.BatchId = id;
            return View(model);
        }
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBatch(int id, AddBatchViewModel model)
        {
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                ViewBag.BatchId = id;
                return View(model);
            }

            var batch = await _context.Batches.FindAsync(id);
            if (batch == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Batch not found." });
                }

                TempData["error"] = "Batch not found.";
                TempData["Error"] = "Batch not found.";
                return RedirectToAction(nameof(Batches));
            }

            // Check for duplicate batch name and year
            var existingBatch = await _context.Batches
                .FirstOrDefaultAsync(b => b.BatchName == model.BatchName && b.Year == model.Year && b.BatchId != id);

            if (existingBatch != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A batch with this name already exists for the selected year." });
                }

                ModelState.AddModelError(nameof(AddBatchViewModel.BatchName), "A batch with this name already exists for the selected year.");
                ViewBag.BatchId = id;
                return View(model);
            }

            batch.BatchName = model.BatchName;
            batch.Year = model.Year;
            batch.IsActive = model.IsActive;

            _context.Batches.Update(batch);
            await _context.SaveChangesAsync();

            var successMessage = $"Batch '{model.BatchName}' has been updated successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            TempData["Success"] = successMessage;
            return RedirectToAction(nameof(Batches));
        }

        // POST: Batch/DeleteBatch
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBatch(int BatchId)
        {
            var batch = await _context.Batches
                .Include(b => b.Students)
                .Include(b => b.CourseAssignments)
                .FirstOrDefaultAsync(b => b.BatchId == BatchId);

            if (batch == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Batch not found." });
                }

                TempData["error"] = "Batch not found.";
                TempData["Error"] = "Batch not found.";
                return RedirectToAction(nameof(Batches));
            }

            // Check if batch has students
            if (batch.Students.Any())
            {
                var message = $"Cannot delete batch '{batch.BatchName}' because it has {batch.Students.Count} student(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                TempData["Error"] = message;
                return RedirectToAction(nameof(Batches));
            }

            // Check if batch has course assignments
            if (batch.CourseAssignments.Any())
            {
                var message = $"Cannot delete batch '{batch.BatchName}' because it has {batch.CourseAssignments.Count} course assignment(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                TempData["Error"] = message;
                return RedirectToAction(nameof(Batches));
            }

            var batchName = batch.BatchName;
            _context.Batches.Remove(batch);
            await _context.SaveChangesAsync();

            var successMessage = $"Batch '{batchName}' has been deleted successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            TempData["Success"] = successMessage;
            return RedirectToAction(nameof(Batches));
        }
    }
}