using GameBot.Domain.Quiz;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The quiz record API (feature 142): list, read, and delete. No route creates or changes a record. Only the
/// <c>quiz-learn</c> step does. The handlers are named methods, because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class QuizEndpoints {
  internal const string NotFoundText = "Quiz record not found.";

  public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.QuizRecords).WithTags("Quiz");

    group.MapGet("", ListAsync)
      .Produces<List<QuizRecord>>(StatusCodes.Status200OK)
      .WithName("ListQuizRecords")
      .WithSummary("List quiz records")
      .WithDescription(
        "Lists the quiz records of all stores. Optional filters store, game, and category combine with AND. "
        + "store and category match without case. game is an exact match. A store file that is damaged is skipped.");

    group.MapGet("{id}", GetAsync)
      .Produces<QuizRecord>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .WithName("GetQuizRecord")
      .WithSummary("Get one quiz record")
      .WithDescription("Gets one quiz record by id. An id that is not valid gives 404.");

    group.MapDelete("{id}", DeleteAsync)
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .WithName("DeleteQuizRecord")
      .WithSummary("Delete one quiz record")
      .WithDescription(
        "Removes one quiz record by id. The next lookup of that question is a guess. "
        + "An id that is not valid gives 404. No route creates or changes a record: only the quiz-learn step does.");

    return app;
  }

  private static async Task<IResult> ListAsync(
      IQuizAnswerRepository repository,
      string? store,
      string? game,
      string? category,
      CancellationToken ct) {
    var records = await repository.ListAsync(store, game, category, ct).ConfigureAwait(false);
    return Results.Ok(records);
  }

  private static async Task<IResult> GetAsync(IQuizAnswerRepository repository, string id, CancellationToken ct) {
    var record = await repository.GetAsync(id, ct).ConfigureAwait(false);
    return record is null ? NotFound() : Results.Ok(record);
  }

  private static async Task<IResult> DeleteAsync(IQuizAnswerRepository repository, string id, CancellationToken ct) {
    return await repository.DeleteAsync(id, ct).ConfigureAwait(false) ? Results.NoContent() : NotFound();
  }

  private static IResult NotFound() =>
    Results.Problem(title: "not_found", detail: NotFoundText, statusCode: StatusCodes.Status404NotFound);
}
