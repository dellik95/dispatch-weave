using System.Collections.Concurrent;
using System.Reflection;

namespace DispatchWeave;

internal static class PipelineOrderResolver
{
    private static readonly ConcurrentDictionary<Type, int> AttributeOrders = new();

    public static int GetOrder(object behavior)
    {
        if (behavior is IOrderedBehavior ordered)
            return ordered.Order;

        return AttributeOrders.GetOrAdd(behavior.GetType(), static type =>
            type.GetCustomAttribute<PipelineOrderAttribute>(inherit: true)?.Order ?? 0);
    }

    public static TBehavior[] MaterializeOrdered<TBehavior>(
        IEnumerable<TBehavior> behaviors,
        bool enableOrdering,
        bool lastBehaviorOutermost = false)
        where TBehavior : class
    {
        var array = behaviors as TBehavior[] ?? behaviors.ToArray();
        if (array.Length <= 1)
            return array;

        if (lastBehaviorOutermost)
        {
            var reversed = new TBehavior[array.Length];
            for (var i = 0; i < array.Length; i++)
                reversed[i] = array[array.Length - 1 - i];
            array = reversed;
        }

        if (!enableOrdering)
            return array;

        // Fast check: only allocate a sort if at least one behavior has a non-zero order
        var hasCustomOrder = false;
        for (var i = 0; i < array.Length; i++)
        {
            if (GetOrder(array[i]) != 0)
            {
                hasCustomOrder = true;
                break;
            }
        }

        if (!hasCustomOrder)
            return array;

        return array.OrderBy(static b => GetOrder(b)).ToArray();
    }
}
