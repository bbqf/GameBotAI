using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Images;
using GameBot.Domain.QueueTemplates;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1812, CA1002, CA1849, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>The packs for the write error tests (issue #333).</summary>
internal static class WriteErrorFixtures {
  public static string V1() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd") },
    images: new[] { GamepackTestKit.Img("img") });

  // Version 2 has the same ids. Every object is an update.
  public static string V2() => GamepackTestKit.Pack("p", "2",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img") },
    images: new[] { GamepackTestKit.Img("img") });
}

/// <summary>Stores that throw an <see cref="IOException"/> on the next write. They keep the real files (issue #333).</summary>
internal sealed class FailOnceStores {
  public FailOnceStores(GamepackTestKit kit) {
    Sequences = new FailOnceSequences(kit.Sequences);
    Commands = new FailOnceCommands(kit.Commands);
    Images = new FailOnceImages(kit.Images);
    Templates = new FailOnceTemplates(kit.Templates);
  }

  public FailOnceSequences Sequences { get; }
  public FailOnceCommands Commands { get; }
  public FailOnceImages Images { get; }
  public FailOnceTemplates Templates { get; }

  /// <summary>The text of the injected I/O error.</summary>
  public string Message { get; set; } = "boom";

  public IOException NewError() => new(Message);

  public GamepackApplier NewApplier(GamepackTestKit kit) {
    Sequences.Owner = this;
    Commands.Owner = this;
    Images.Owner = this;
    Templates.Owner = this;
    return new GamepackApplier(Sequences, Commands, Images, kit.Store, kit.Gate, takeOverBackups: kit.TakeOverBackups, templates: Templates, alternates: kit.Alternates);
  }
}

internal sealed class FailOnceSequences : ISequenceRepository {
  private readonly ISequenceRepository _inner;
  public FailOnceSequences(ISequenceRepository inner) => _inner = inner;
  public FailOnceStores? Owner { get; set; }
  public bool FailNext { get; set; }
  public string? GetFilePath(string id) => _inner.GetFilePath(id);
  public Task<CommandSequence?> GetAsync(string id) => _inner.GetAsync(id);
  public Task<IReadOnlyList<CommandSequence>> ListAsync() => _inner.ListAsync();
  public Task<bool> DeleteAsync(string id) => _inner.DeleteAsync(id);

  public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Guard(() => _inner.CreateAsync(sequence));
  public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Guard(() => _inner.UpdateAsync(sequence));

  private Task<CommandSequence> Guard(Func<Task<CommandSequence>> write) {
    if (FailNext) {
      FailNext = false;
      throw (Owner?.NewError() ?? new IOException("boom"));
    }

    return write();
  }
}

internal sealed class FailOnceCommands : ICommandRepository {
  private readonly ICommandRepository _inner;
  public FailOnceCommands(ICommandRepository inner) => _inner = inner;
  public FailOnceStores? Owner { get; set; }
  public bool FailNext { get; set; }
  public string? GetFilePath(string id) => _inner.GetFilePath(id);
  public Task<Command?> GetAsync(string id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
  public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => _inner.ListAsync(ct);
  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => _inner.DeleteAsync(id, ct);

  public Task<Command> AddAsync(Command command, CancellationToken ct = default) {
    if (FailNext) {
      FailNext = false;
      throw (Owner?.NewError() ?? new IOException("boom"));
    }

    return _inner.AddAsync(command, ct);
  }

  public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) {
    if (FailNext) {
      FailNext = false;
      throw (Owner?.NewError() ?? new IOException("boom"));
    }

    return _inner.UpdateAsync(command, ct);
  }
}

internal sealed class FailOnceImages : IImageRepository {
  private readonly IImageRepository _inner;
  public FailOnceImages(IImageRepository inner) => _inner = inner;
  public FailOnceStores? Owner { get; set; }
  public bool FailNext { get; set; }
  public string? GetFilePath(string id) => _inner.GetFilePath(id);
  public Task<IReadOnlyCollection<string>> ListIdsAsync(CancellationToken ct = default) => _inner.ListIdsAsync(ct);
  public Task<ImageAsset?> GetAsync(string id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
  public Task<Stream?> OpenReadAsync(string id, CancellationToken ct = default) => _inner.OpenReadAsync(id, ct);
  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => _inner.DeleteAsync(id, ct);
  public Task<bool> ExistsAsync(string id, CancellationToken ct = default) => _inner.ExistsAsync(id, ct);

  public Task<ImageAsset> SaveAsync(string id, Stream content, string contentType, string? filename, bool overwrite, CancellationToken ct = default) {
    if (FailNext) {
      FailNext = false;
      throw (Owner?.NewError() ?? new IOException("boom"));
    }

    return _inner.SaveAsync(id, content, contentType, filename, overwrite, ct);
  }
}

internal sealed class FailOnceTemplates : IQueueTemplateRepository {
  private readonly IQueueTemplateRepository _inner;
  public FailOnceTemplates(IQueueTemplateRepository inner) => _inner = inner;
  public FailOnceStores? Owner { get; set; }
  public bool FailNext { get; set; }
  public string? GetFilePath(string id) => _inner.GetFilePath(id);
  public Task<QueueTemplate?> GetAsync(string id) => _inner.GetAsync(id);
  public Task<IReadOnlyList<QueueTemplate>> ListAsync() => _inner.ListAsync();
  public Task<QueueTemplate?> FindByNameAsync(string name) => _inner.FindByNameAsync(name);
  public Task<QueueTemplate> UpdateAsync(QueueTemplate item) => _inner.UpdateAsync(item);
  public Task<bool> DeleteAsync(string id) => _inner.DeleteAsync(id);

  public Task<QueueTemplate> CreateAsync(QueueTemplate item) {
    if (FailNext) {
      FailNext = false;
      throw (Owner?.NewError() ?? new IOException("boom"));
    }

    return _inner.CreateAsync(item);
  }
}
