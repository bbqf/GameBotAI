namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for setting or clearing a queue's linked template.
  /// <see cref="TemplateId"/> is the stable template ID to link, or null to clear the link.
  /// </summary>
  internal sealed class SetQueueTemplateLinkRequest {
    public string? TemplateId { get; set; }
  }
}
