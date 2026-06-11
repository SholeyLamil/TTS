using Microsoft.AspNetCore.Mvc;
using Tts.Api.Models;
using Tts.Api.Queue;

namespace Tts.Api.Controllers;

/// <summary>
/// Ingestion endpoint for transaction telemetry. Fire-and-forget: it validates, queues, and
/// acknowledges with 202 immediately — it never waits on InfluxDB.
/// </summary>
[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly IEventQueue _queue;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IEventQueue queue, ILogger<EventsController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <summary>POST /api/events — accept a transaction event for asynchronous storage.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitEvent([FromBody] TransactionEventDto dto)
    {
        // [ApiController] already returns 400 for invalid models; this is a defensive double-check.
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _queue.EnqueueAsync(dto, HttpContext.RequestAborted);
        _logger.LogInformation(
            "Accepted event {EventType} for transaction {TransactionId}", dto.EventType, dto.TransactionId);

        return Accepted(new { message = "received" });
    }
}
