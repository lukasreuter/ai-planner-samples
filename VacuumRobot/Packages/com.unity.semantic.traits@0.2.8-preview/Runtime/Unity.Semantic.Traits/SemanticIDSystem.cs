using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.Semantic.Traits
{
public partial class SemanticIDSystem : SystemBase
{
    protected override void OnUpdate()
    {

    }

    public static Entity FindSemanticID(SemanticID semanticID, EntityManager entityManager)
    {
        //TODO: cache this in the system itself? or maybe just have this in the editor and skip the lookup in build and always create a new entity
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SemanticID>());
        using var components = query.ToComponentDataArray<SemanticID>(Allocator.TempJob);
        using var entities = query.ToEntityArray(Allocator.TempJob);
        using var result = new NativeReference<Entity>(Entity.Null, Allocator.TempJob);

        new SemanticIDSearchJob
        {
            target = semanticID,
            ids = components,
            entities = entities,
            result = result,
        }.Run();

        return result.Value;
    }

    [BurstCompile]
    private struct SemanticIDSearchJob : IJob
    {
        public SemanticID target;

        [ReadOnly]
        public NativeArray<SemanticID> ids;
        [ReadOnly]
        public NativeArray<Entity> entities;
        [WriteOnly]
        public NativeReference<Entity> result;

        public void Execute()
        {
            var length = ids.Length;
            for (var i = 0; i < length; ++i)
            {
                if (ids[i] == target)
                {
                    result.Value = entities[i];
                    break;
                }
            }
        }
    }
}
}
