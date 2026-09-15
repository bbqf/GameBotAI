using GameBot.Service.Contracts.Sequences;
using GameBot.Service.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using SequencesAuthoringDeepLinkDto = GameBot.Service.Contracts.Sequences.AuthoringDeepLinkDto;
using SequencesConditionEvaluationTraceDto = GameBot.Service.Contracts.Sequences.ConditionEvaluationTraceDto;

namespace GameBot.Service.Swagger;

internal sealed class ConditionalFlowSchemaDocumentFilter : IDocumentFilter {
  public void Apply(Microsoft.OpenApi.Models.OpenApiDocument swaggerDoc, DocumentFilterContext context) {
    _ = swaggerDoc;
    context.SchemaGenerator.GenerateSchema(typeof(SequenceFlowUpsertRequestDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceFlowDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(FlowStepDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(BranchLinkDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ConditionExpressionDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ConditionOperandDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceSaveConflictDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequencesConditionEvaluationTraceDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequencesAuthoringDeepLinkDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceUpsertContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequencePatchContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceStepContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(DelayRangeMsContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(PrimitiveActionRequest), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceStepConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ImageVisibleConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(CommandOutcomeConditionContract), context.SchemaRepository);
    // Feature 088: composite conditions, so the recursive children array and the three rule
    // discriminators are discoverable from the published schema rather than only at run time.
    context.SchemaGenerator.GenerateSchema(typeof(CompositeConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(AllConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(AnyConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(NoneConditionContract), context.SchemaRepository);

    AliasSchema(context, nameof(SequenceFlowUpsertRequestDto), "SequenceFlowUpsertRequest");
    AliasSchema(context, nameof(SequenceFlowDto), "SequenceFlow");
    AliasSchema(context, nameof(ConditionExpressionDto), "ConditionExpression");
    AliasSchema(context, nameof(ConditionOperandDto), "ConditionOperand");
    AliasSchema(context, nameof(SequenceSaveConflictDto), "SequenceSaveConflict");
    AliasSchema(context, nameof(SequencesConditionEvaluationTraceDto), "ConditionEvaluationTrace");
    AliasSchema(context, nameof(SequencesAuthoringDeepLinkDto), "AuthoringDeepLink");
    AliasSchema(context, nameof(SequenceUpsertContract), "SequenceUpsertRequest");
    AliasSchema(context, nameof(SequenceStepContract), "SequenceStep");
    AliasSchema(context, nameof(PrimitiveActionRequest), "PrimitiveAction");
    AliasSchema(context, nameof(SequenceStepConditionContract), "SequenceStepCondition");
    AliasSchema(context, nameof(ImageVisibleConditionContract), "ImageVisibleCondition");
    AliasSchema(context, nameof(CommandOutcomeConditionContract), "CommandOutcomeCondition");
    AliasSchema(context, nameof(CompositeConditionContract), "CompositeCondition");
    AliasSchema(context, nameof(AllConditionContract), "AllCondition");
    AliasSchema(context, nameof(AnyConditionContract), "AnyCondition");
    AliasSchema(context, nameof(NoneConditionContract), "NoneCondition");
  }

  private static void AliasSchema(DocumentFilterContext context, string sourceName, string aliasName) {
    if (context.SchemaRepository.Schemas.TryGetValue(sourceName, out var schema)) {
      context.SchemaRepository.Schemas[aliasName] = schema;
    }
  }
}
