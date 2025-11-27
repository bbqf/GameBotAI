using System.Drawing;

namespace GameBot.Domain.Triggers.Evaluators;

public interface IReferenceImageStore
{
    bool TryGet(string id, out Bitmap bitmap);
    void AddOrUpdate(string id, Bitmap bitmap);
    bool Exists(string id);
    bool Delete(string id);
}