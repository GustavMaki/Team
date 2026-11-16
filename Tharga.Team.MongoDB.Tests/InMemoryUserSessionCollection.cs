using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// An <see cref="IUserSessionCollection"/> that keeps its documents in memory and applies the filters and updates the store sends, so the store's conditional
/// writes can be run for real, from two store instances over one collection, without a database.
/// </summary>
/// <remarks>
/// <b>It evaluates the rendered filter and update, so it checks what the store asks the database to do, not what the store intends.</b> It understands the
/// operators a conditional session write needs (equality, <c>$ne $lt $lte $gt $gte $in $nin $exists $and $or $not</c>, and <c>$set $unset $max $min</c>) and
/// throws on anything else, so a new operator is a decision to extend it. Each operation holds one lock, as one MongoDB document update is atomic.
/// A missing field matches <c>null</c>, as it does in MongoDB. The calls it answers are the ones a store of this shape makes: add, try-add (unique on
/// <c>SessionId</c>), get, get-one, update-one, update-many and count, by filter or by predicate.
/// </remarks>
internal sealed class InMemoryUserSessionCollection
{
    private readonly object _gate = new();
    private readonly List<BsonDocument> _documents = [];

    public InMemoryUserSessionCollection()
    {
        Collection = Substitute.For<IUserSessionCollection>();

        Collection.AddAsync(Arg.Any<UserSessionEntity>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Insert(call.Arg<UserSessionEntity>(), throwOnDuplicate: true)));
        Collection.TryAddAsync(Arg.Any<UserSessionEntity>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Insert(call.Arg<UserSessionEntity>(), throwOnDuplicate: false)));

        Collection.GetAsync(Arg.Any<FilterDefinition<UserSessionEntity>>(), Arg.Any<Options<UserSessionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Read(call.Arg<FilterDefinition<UserSessionEntity>>()));
        Collection.GetAsync(Arg.Any<Expression<Func<UserSessionEntity, bool>>>(), Arg.Any<Options<UserSessionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Read(Builders<UserSessionEntity>.Filter.Where(call.Arg<Expression<Func<UserSessionEntity, bool>>>())));

        Collection.GetOneAsync(Arg.Any<FilterDefinition<UserSessionEntity>>(), Arg.Any<OneOption<UserSessionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(First(call.Arg<FilterDefinition<UserSessionEntity>>())));
        Collection.GetOneAsync(Arg.Any<Expression<Func<UserSessionEntity, bool>>>(), Arg.Any<OneOption<UserSessionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(First(Builders<UserSessionEntity>.Filter.Where(call.Arg<Expression<Func<UserSessionEntity, bool>>>()))));

        Collection.UpdateOneAsync(Arg.Any<FilterDefinition<UserSessionEntity>>(), Arg.Any<UpdateDefinition<UserSessionEntity>>(), Arg.Any<OneOption<UserSessionEntity>>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UpdateOne(call.Arg<FilterDefinition<UserSessionEntity>>(), call.Arg<UpdateDefinition<UserSessionEntity>>())));
        Collection.UpdateOneAsync(Arg.Any<Expression<Func<UserSessionEntity, bool>>>(), Arg.Any<UpdateDefinition<UserSessionEntity>>(), Arg.Any<OneOption<UserSessionEntity>>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UpdateOne(Builders<UserSessionEntity>.Filter.Where(call.Arg<Expression<Func<UserSessionEntity, bool>>>()), call.Arg<UpdateDefinition<UserSessionEntity>>())));

        Collection.UpdateManyAsync(Arg.Any<FilterDefinition<UserSessionEntity>>(), Arg.Any<UpdateDefinition<UserSessionEntity>>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UpdateMany(call.Arg<FilterDefinition<UserSessionEntity>>(), call.Arg<UpdateDefinition<UserSessionEntity>>())));
        Collection.UpdateManyAsync(Arg.Any<Expression<Func<UserSessionEntity, bool>>>(), Arg.Any<UpdateDefinition<UserSessionEntity>>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UpdateMany(Builders<UserSessionEntity>.Filter.Where(call.Arg<Expression<Func<UserSessionEntity, bool>>>()), call.Arg<UpdateDefinition<UserSessionEntity>>())));

        Collection.CountAsync(Arg.Any<FilterDefinition<UserSessionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Count(call.Arg<FilterDefinition<UserSessionEntity>>())));
        Collection.CountAsync(Arg.Any<Expression<Func<UserSessionEntity, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Count(Builders<UserSessionEntity>.Filter.Where(call.Arg<Expression<Func<UserSessionEntity, bool>>>()))));
    }

    /// <summary>What the store is given.</summary>
    public IUserSessionCollection Collection { get; }

    /// <summary>The stored documents, as the database would hold them.</summary>
    public IReadOnlyList<BsonDocument> Documents
    {
        get
        {
            lock (_gate) return [.. _documents.Select(x => x.DeepClone().AsBsonDocument)];
        }
    }

    /// <summary>The stored document of a session, or null.</summary>
    public BsonDocument Document(string sessionId) => Documents.SingleOrDefault(x => x["SessionId"] == sessionId);

    /// <summary>Puts a document in as it is, as a session left by an earlier version of the package would be.</summary>
    public void Seed(UserSessionEntity entity)
    {
        lock (_gate) _documents.Add(entity.ToBsonDocument());
    }

    private bool Insert(UserSessionEntity entity, bool throwOnDuplicate)
    {
        lock (_gate)
        {
            if (_documents.Any(x => x["SessionId"] == entity.SessionId))
            {
                if (throwOnDuplicate) throw new InvalidOperationException("Duplicate SessionId.");
                return false;
            }

            _documents.Add(entity.ToBsonDocument());
            return true;
        }
    }

    private async IAsyncEnumerable<UserSessionEntity> Read(FilterDefinition<UserSessionEntity> filter)
    {
        UserSessionEntity[] found;
        lock (_gate) found = [.. _documents.Where(x => Matches(x, Render(filter))).Select(ToEntity)];

        foreach (var entity in found)
        {
            await Task.Yield();
            yield return entity;
        }
    }

    private UserSessionEntity First(FilterDefinition<UserSessionEntity> filter)
    {
        lock (_gate)
        {
            var document = _documents.FirstOrDefault(x => Matches(x, Render(filter)));
            return document == null ? null : ToEntity(document);
        }
    }

    private EntityChangeResult<UserSessionEntity> UpdateOne(FilterDefinition<UserSessionEntity> filter, UpdateDefinition<UserSessionEntity> update)
    {
        lock (_gate)
        {
            var document = _documents.FirstOrDefault(x => Matches(x, Render(filter)));
            if (document == null) return Changed(null, null);

            var before = ToEntity(document);
            Apply(document, update);
            return Changed(before, ToEntity(document));
        }
    }

    private long UpdateMany(FilterDefinition<UserSessionEntity> filter, UpdateDefinition<UserSessionEntity> update)
    {
        lock (_gate)
        {
            var matching = _documents.Where(x => Matches(x, Render(filter))).ToArray();
            foreach (var document in matching) Apply(document, update);
            return matching.Length;
        }
    }

    private long Count(FilterDefinition<UserSessionEntity> filter)
    {
        lock (_gate) return _documents.Count(x => Matches(x, Render(filter)));
    }

    private static UserSessionEntity ToEntity(BsonDocument document) => BsonSerializer.Deserialize<UserSessionEntity>(document.DeepClone().AsBsonDocument);

    private static EntityChangeResult<UserSessionEntity> Changed(UserSessionEntity before, UserSessionEntity after)
        => (EntityChangeResult<UserSessionEntity>)typeof(EntityChangeResult<UserSessionEntity>)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(UserSessionEntity), typeof(UserSessionEntity)])!
            .Invoke([before, after]);

    private static BsonDocument Render(FilterDefinition<UserSessionEntity> filter)
        => filter.Render(new RenderArgs<UserSessionEntity>(BsonSerializer.LookupSerializer<UserSessionEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    private static void Apply(BsonDocument document, UpdateDefinition<UserSessionEntity> update)
    {
        var rendered = update.Render(new RenderArgs<UserSessionEntity>(BsonSerializer.LookupSerializer<UserSessionEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

        foreach (var op in rendered)
        {
            foreach (var field in op.Value.AsBsonDocument)
            {
                switch (op.Name)
                {
                    case "$set":
                        document[field.Name] = field.Value;
                        break;
                    case "$unset":
                        document.Remove(field.Name);
                        break;
                    case "$max":
                        if (!document.Contains(field.Name) || Compare(document[field.Name], field.Value) < 0) document[field.Name] = field.Value;
                        break;
                    case "$min":
                        if (!document.Contains(field.Name) || Compare(document[field.Name], field.Value) > 0) document[field.Name] = field.Value;
                        break;
                    default:
                        throw new NotSupportedException($"The in-memory collection does not understand the update operator {op.Name}.");
                }
            }
        }
    }

    private static bool Matches(BsonDocument document, BsonDocument filter)
    {
        foreach (var condition in filter)
        {
            switch (condition.Name)
            {
                case "$and":
                    if (!condition.Value.AsBsonArray.All(x => Matches(document, x.AsBsonDocument))) return false;
                    break;
                case "$or":
                    if (!condition.Value.AsBsonArray.Any(x => Matches(document, x.AsBsonDocument))) return false;
                    break;
                case "$nor":
                    if (condition.Value.AsBsonArray.Any(x => Matches(document, x.AsBsonDocument))) return false;
                    break;
                default:
                    if (!FieldMatches(document.GetValue(condition.Name, BsonNull.Value), condition.Value)) return false;
                    break;
            }
        }

        return true;
    }

    private static bool FieldMatches(BsonValue actual, BsonValue expected)
    {
        if (expected is BsonDocument operators && operators.ElementCount > 0 && operators.Names.All(x => x.StartsWith('$')))
        {
            foreach (var op in operators)
            {
                var ok = op.Name switch
                {
                    "$eq" => Equal(actual, op.Value),
                    "$ne" => !Equal(actual, op.Value),
                    "$lt" => !actual.IsBsonNull && Compare(actual, op.Value) < 0,
                    "$lte" => !actual.IsBsonNull && Compare(actual, op.Value) <= 0,
                    "$gt" => !actual.IsBsonNull && Compare(actual, op.Value) > 0,
                    "$gte" => !actual.IsBsonNull && Compare(actual, op.Value) >= 0,
                    "$in" => op.Value.AsBsonArray.Any(x => Equal(actual, x)),
                    "$nin" => !op.Value.AsBsonArray.Any(x => Equal(actual, x)),
                    "$exists" => op.Value.ToBoolean() == !actual.IsBsonNull,
                    "$not" => !FieldMatches(actual, op.Value),
                    _ => throw new NotSupportedException($"The in-memory collection does not understand the filter operator {op.Name}.")
                };

                if (!ok) return false;
            }

            return true;
        }

        return Equal(actual, expected);
    }

    private static bool Equal(BsonValue a, BsonValue b) => (a.IsBsonNull && b.IsBsonNull) || (!a.IsBsonNull && !b.IsBsonNull && Compare(a, b) == 0);

    private static int Compare(BsonValue a, BsonValue b) => a.CompareTo(b);
}
