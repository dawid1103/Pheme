using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shop.Common.Errors;

namespace Shop.Api.Errors
{
    /// <summary>
    /// Turns the application's expected exceptions into problem details: <see cref="ValidationException"/> into 400
    /// with the errors per field, <see cref="ForbiddenException"/> into 403, <see cref="NotFoundException"/> into 404,
    /// <see cref="BusinessRuleException"/> into 422,
    /// and a save conflicting with another request (<see cref="IsConflict"/>) into 409. Other exceptions stay unhandled
    /// (500).
    /// </summary>
    /// <remarks>
    /// Pheme only publishes the domain events of a save; what the save throws reaches the caller unchanged, so mapping
    /// it is the application's job, as here.
    /// </remarks>
    internal sealed class ApiExceptionFilter(ProblemDetailsFactory problemDetailsFactory) : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            ProblemDetails? problem = context.Exception switch
            {
                ValidationException exception => CreateValidationProblem(context.HttpContext, exception),
                ForbiddenException exception => problemDetailsFactory.CreateProblemDetails(
                    context.HttpContext, StatusCodes.Status403Forbidden, detail: exception.Message),
                NotFoundException exception => problemDetailsFactory.CreateProblemDetails(
                    context.HttpContext, StatusCodes.Status404NotFound, detail: exception.Message),
                BusinessRuleException exception => problemDetailsFactory.CreateProblemDetails(
                    context.HttpContext, StatusCodes.Status422UnprocessableEntity, detail: exception.Message),

                { } exception when IsConflict(exception) => problemDetailsFactory.CreateProblemDetails(
                    context.HttpContext,
                    StatusCodes.Status409Conflict,
                    detail: "The data changed while the request was handled; nothing was saved. Try again."),
                _ => null,
            };
            if (problem is null)
            {
                return;
            }

            context.Result = new ObjectResult(problem)
            {
                StatusCode = problem.Status,
                ContentTypes = { "application/problem+json" },
            };
            context.ExceptionHandled = true;
        }

        /// <summary>
        /// Tells whether a save failed because another request changed the same data since the handler read it: a row
        /// version that no longer matches (two orders of the last items of a product), or a unique index another save
        /// filled first (two entries numbered alike in the history of one order). Nothing was saved, and the client may
        /// try again.
        /// </summary>
        public static bool IsConflict(Exception exception) =>
            exception is DbUpdateConcurrencyException
                or DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

        private ValidationProblemDetails CreateValidationProblem(HttpContext httpContext, ValidationException exception)
        {
            var modelState = new ModelStateDictionary();
            foreach ((string field, string[] messages) in exception.Errors)
            {
                foreach (string message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            return problemDetailsFactory.CreateValidationProblemDetails(httpContext, modelState, StatusCodes.Status400BadRequest);
        }
    }
}
