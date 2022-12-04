using Unity.Entities;

namespace Unity.Semantic.Traits
{
public interface ITraitConversion
{
    void RuntimeConvert(Entity entity, EntityManager dstManager);
}
}
