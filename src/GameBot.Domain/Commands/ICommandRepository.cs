namespace GameBot.Domain.Commands;

public interface ICommandRepository {
  Task<Command> AddAsync(Command command, CancellationToken ct = default);
  Task<Command?> GetAsync(string id, CancellationToken ct = default);
  Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default);
  Task<Command?> UpdateAsync(Command command, CancellationToken ct = default);
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
