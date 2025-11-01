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

        public IActionResult Index()
        {
            return View();
        }


        [HttpGet]

        public async Task<IActionResult> Batches()
        {
            var batches = await _context.Batches
        .Include(b => b.Students)  // ← This loads the students
        .OrderByDescending(b => b.Year)
        .ThenBy(b => b.BatchName)
        .ToListAsync();

            return View(batches);
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
        public async Task<IActionResult> AddBatch(AddBatchViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check if batch name already exists for the same year
                var existingBatch = await _context.Batches
                    .FirstOrDefaultAsync(b => b.BatchName == model.BatchName && b.Year == model.Year);

                if (existingBatch != null)
                {
                    ModelState.AddModelError("BatchName", "A batch with this name already exists for the selected year.");
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
                TempData["success"] = $"Batch '{model.BatchName}' has been added successfully!";
                return RedirectToAction("Batches");
            }

            return View(model);
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

            return View(model);
        }
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> EditBatch(int id, AddBatchViewModel model)
        {
            if (ModelState.IsValid)
            {
                var batch = await _context.Batches.FindAsync(id);
                if (batch == null)
                {
                    return NotFound();
                }

                // Check for duplicate batch name and year
                var existingBatch = await _context.Batches
                    .FirstOrDefaultAsync(b => b.BatchName == model.BatchName && b.Year == model.Year && b.BatchId != id);

                if (existingBatch != null)
                {
                    ModelState.AddModelError("BatchName", "A batch with this name already exists for the selected year.");
                    return View(model);
                }

                batch.BatchName = model.BatchName;
                batch.Year = model.Year;
                batch.IsActive = model.IsActive;

                _context.Batches.Update(batch);
                await _context.SaveChangesAsync();
                TempData["success"] = $"Batch '{model.BatchName}' has been updated successfully!";
                return RedirectToAction("Batches");
            }

            return View(model);

        }
    }
}