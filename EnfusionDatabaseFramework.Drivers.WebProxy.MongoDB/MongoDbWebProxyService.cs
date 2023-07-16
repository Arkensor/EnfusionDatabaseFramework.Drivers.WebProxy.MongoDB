using EnfusionDatabaseFramework.Drivers.WebProxy.Core;
using EnfusionDatabaseFramework.Drivers.WebProxy.Core.Conditions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB;

public class MongoDbWebProxyService : IDbWebProxyService
{
    protected readonly IMongoClient _mongoClient;

    public MongoDbWebProxyService(IMongoClient mongoClient)
    {
        BsonTypeMapper.RegisterCustomTypeMapper(typeof(Typename), new TypenameBsonTypeMapper());
        _mongoClient = mongoClient;
    }

    public async Task AddOrUpdateAsync(string database, string collection, Guid id, string data, CancellationToken cancellationToken)
    {
        var document = BsonDocument.Parse(data);
        document["_id"] = document["m_sId"];

        var mongoDb = _mongoClient.GetDatabase(database);
        var mongoCollection = mongoDb.GetCollection<BsonDocument>(collection);

        var filter = Builders<BsonDocument>.Filter.Eq("_id", id.ToString());

        var updateOptions = new ReplaceOptions()
        {
            IsUpsert = true
        };

        var result = await mongoCollection.ReplaceOneAsync(filter, document, updateOptions, cancellationToken);

        if (!result.IsAcknowledged || (result.MatchedCount == 0 && result.ModifiedCount == 0 && result.UpsertedId == null))
            throw new ProxyRequestException(500, "Failed to insert/update the data");
    }

    public async Task RemoveAsync(string database, string collection, Guid id, CancellationToken cancellationToken)
    {
        var mongoDb = _mongoClient.GetDatabase(database);
        var mongoCollection = mongoDb.GetCollection<BsonDocument>(collection);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id.ToString());
        var result = await mongoCollection.DeleteOneAsync(filter, cancellationToken);

        if (result.DeletedCount == 0)
            throw new ProxyRequestException(404, "Not found.");
    }

    public async Task<IEnumerable<string>> FindAllAsync(string database, string collection, DbFindRequest findRequest, CancellationToken cancellationToken)
    {
        var mongoDb = _mongoClient.GetDatabase(database);
        var mongoCollection = mongoDb.GetCollection<BsonDocument>(collection);

        var condition = findRequest.Condition is null ?
            new BsonDocument() :
            new BsonDocument("$expr", TranslateCondition(findRequest.Condition));

        var sort = TranslateOrderBy(findRequest.OrderBy);

        var documents = await mongoCollection
            .Find(condition)
            .Sort(sort)
            .Skip(findRequest.Offset)
            .Limit(findRequest.Limit)
            .ToListAsync(cancellationToken);

        return documents.Select(x =>
        {
            x.Remove("_id");
            return x.ToJson();
        });
    }

    protected BsonDocument? TranslateOrderBy(List<List<string>>? orderBy)
    {
        if (orderBy == null)
            return null;

        var sortConditions = new BsonDocument();
        foreach (var pair in orderBy)
        {
            if (pair.Count != 2) continue;
            var sortKey = pair[0].Trim();
            var sortDir = pair[1].Trim();
            sortConditions.AddRange(new BsonDocument(sortKey, sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase) ? 1 : -1));
        }

        if (sortConditions.ElementCount == 0)
            return null;

        return sortConditions;
    }

    protected BsonDocument TranslateCondition(DbFindCondition condition)
    {
        if (condition is DbFindConditionWithChildren conditionWithChildren)
        {
            return new BsonDocument(condition is DbFindOr ? "$or" : "$and", new BsonArray(conditionWithChildren.Conditions.Select(TranslateCondition)));
        }

        if (condition is DbFindFieldCondition findFieldCondition)
        {
            // Rely on vanilla _id index for search
            if (findFieldCondition.FieldPath == "m_sId")
                findFieldCondition.FieldPath = "_id";

            return TranslateFieldCondition(findFieldCondition);
        }

        throw new NotImplementedException();
    }

    protected BsonDocument TranslateFieldCondition(DbFindFieldCondition fieldCondition, int currentSegment = 0)
    {
        var segments = fieldCondition.AsParsed.Segments
            .Skip(currentSegment)
            .TakeWhile(x =>
                (x.Modifiers & (
                    DbFindFieldConditionPathModifier.ANY |
                    DbFindFieldConditionPathModifier.ALL |
                    DbFindFieldConditionPathModifier.KEYS |
                    DbFindFieldConditionPathModifier.VALUES)) == 0
            )
            .ToList();
        var terminatorSegment = fieldCondition.AsParsed.Segments.ElementAtOrDefault(currentSegment + segments.Count);
        if (terminatorSegment is not null)
        {
            segments.Add(terminatorSegment);
        }
        else
        {
            terminatorSegment = segments.Last();
        }

        var fieldPath = string.Join('.', segments.Where(x => !string.IsNullOrWhiteSpace(x.FieldName)).Select(x => x.FieldName));
        if (currentSegment > 0) fieldPath = $"$this{(!string.IsNullOrEmpty(fieldPath) ? "." : string.Empty)}{fieldPath}";
        fieldPath = $"${fieldPath}";

        var input = BsonValue.Create(fieldPath);
        var arrayMode = false;
        var anyMatch = false;

        BsonDocument condition;
        var nextSegment = currentSegment + segments.Count;
        var expandCondition = nextSegment < fieldCondition.AsParsed.Segments.Count;

        if (terminatorSegment.Modifiers != 0)
        {
            // Handle map key or value input processing
            var keysAccessor = terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.KEYS);
            if (keysAccessor || terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.VALUES))
            {
                input = new BsonDocument("$map", new BsonDocument
                {
                    { "input", new BsonDocument("$objectToArray", input) },
                    { "in", $"$$this.{(keysAccessor ? "k" : "v")}" }
                });
            }

            // Either modifier 'ANY' is set explicitly or altertively no 'ALL' modiffier is present also, which would fallback to 'ANY' then.
            anyMatch = terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.ANY) ||
                (expandCondition && !terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.ALL));

            arrayMode = anyMatch || terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.ALL);
        }

        if (expandCondition)
        {
            condition = TranslateFieldCondition(fieldCondition, nextSegment);
        }
        else
        {
            var getter = arrayMode ? BsonValue.Create("$$this") : input;
            if (terminatorSegment.Modifiers != 0)
            {
                if (terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.COUNT))
                {
                    getter = new BsonDocument("$size", getter);
                }
                else if (terminatorSegment.Modifiers.HasFlag(DbFindFieldConditionPathModifier.LENGTH))
                {
                    getter = new BsonDocument("$strLenCP", getter);
                }
            }

            switch (fieldCondition)
            {
                case DbFindCheckFieldNullOrDefault dbFindCheckFieldEmpty:
                {
                    condition = new BsonDocument("$in", new BsonArray
                    {
                        getter,
                        new BsonArray
                        {
                            BsonNull.Value, 0, false, "", new BsonArray(), new BsonArray{0, 0, 0}
                        }
                    });

                    if (!dbFindCheckFieldEmpty.ShouldBeNullOrDefault)
                    {
                        condition = new BsonDocument("$not", condition);
                    }

                    break;
                }

                case DbFindCompareFieldValues<int> compareInt:
                {
                    condition = TranslateExpression(compareInt, getter);
                    break;
                }

                case DbFindCompareFieldValues<List<int>> compareIntArray:
                {
                    condition = TranslateExpression(compareIntArray, getter);
                    break;
                }

                case DbFindCompareFieldValues<double> compareFloat:
                {
                    condition = TranslateExpression(compareFloat, getter);
                    break;
                }

                case DbFindCompareFieldValues<List<double>> compareFloatArray:
                {
                    condition = TranslateExpression(compareFloatArray, getter);
                    break;
                }

                case DbFindCompareFieldValues<bool> compareBool:
                {
                    condition = TranslateExpression(compareBool, getter);
                    break;
                }

                case DbFindCompareFieldValues<List<bool>> compareBoolArray:
                {
                    condition = TranslateExpression(compareBoolArray, getter);
                    break;
                }

                case DbFindCompareFieldValues<string> compareString:
                {
                    if (compareString.StringsPartialMatches)
                    {
                        if (compareString.ComparisonOperator == DbFindOperator.EQUAL)
                        {
                            compareString.ComparisonOperator = DbFindOperator.CONTAINS;
                        }
                        else if (compareString.ComparisonOperator == DbFindOperator.NOT_EQUAL)
                        {
                            compareString.ComparisonOperator = DbFindOperator.NOT_CONTAINS;
                        }
                    }

                    if (compareString.ComparisonOperator >= DbFindOperator.CONTAINS &&
                        compareString.ComparisonOperator <= DbFindOperator.NOT_CONTAINS_ALL)
                    {
                        var stringGetter = getter;
                        var arrayGetter = getter;
                        if (compareString.StringsInvariant)
                        {
                            compareString.ComparisonValues = compareString.ComparisonValues.Select(x => x.ToLowerInvariant()).ToList();
                            stringGetter = new BsonDocument("$toLower", getter);
                            arrayGetter = new BsonDocument("$map", new BsonDocument()
                            {
                                { "input", getter },
                                { "in", new BsonDocument("$toLower", "$$this") }
                            });
                        }

                        BsonDocument CreateCompareValueExpression(BsonValue valueGetter)
                        {
                            return new BsonDocument(
                                compareString.ComparisonOperator == DbFindOperator.CONTAINS ||
                                compareString.ComparisonOperator == DbFindOperator.CONTAINS_ALL ? "$ne" : "$eq", new BsonArray
                                {
                                    new BsonDocument("$indexOfCP", new BsonArray
                                    {
                                        valueGetter,
                                        "$$compareValue"
                                    }),
                                    -1
                                });
                        }

                        BsonDocument CreateMapExpression(BsonValue inExpression)
                        {
                            return new BsonDocument(compareString.ComparisonOperator < DbFindOperator.CONTAINS_ALL ? "$anyElementTrue" : "$allElementsTrue",
                                new BsonDocument("$map", new BsonDocument()
                                {
                                    { "input", BsonValue.Create(compareString.ComparisonValues) },
                                    { "as", "compareValue" },
                                    { "in", inExpression}
                                }));
                        }

                        condition = new BsonDocument("$cond", new BsonDocument()
                        {
                            { "if", new BsonDocument("$isArray", getter) },
                            { "then", compareString.StringsPartialMatches ?
                                CreateMapExpression(new BsonDocument("$anyElementTrue", new BsonDocument("$map", new BsonDocument()
                                {
                                    { "input", arrayGetter },
                                    { "in", CreateCompareValueExpression("$$this")}
                                }))) : TranslateExpression(compareString, arrayGetter)
                            },
                            { "else", CreateMapExpression(CreateCompareValueExpression(stringGetter))},
                        });
                    }
                    else
                    {
                        condition = TranslateExpression(compareString, compareString.StringsInvariant ? new BsonDocument("$toLower", getter) : getter);
                    }

                    break;
                }

                case DbFindCompareFieldValues<List<string>> compareStringArray:
                {
                    if (compareStringArray.ComparisonOperator > DbFindOperator.NOT_EQUAL)
                    {
                        throw new NotImplementedException("Currently array<string> operations only support EQ/NE operator.");
                    }

                    if (compareStringArray.StringsInvariant)
                    {
                        compareStringArray.ComparisonValues = compareStringArray.ComparisonValues.Select(x => x.Select(y => y.ToLowerInvariant()).ToList()).ToList();
                    }

                    if (compareStringArray.StringsPartialMatches)
                    {
                        BsonDocument CreateOuterExpression(List<List<string>> comparisonValues, int currentIndex = 0)
                        {
                            return new BsonDocument("$cond", new BsonDocument()
                            {
                                { "if", CreateArrayEqualityExpression(comparisonValues[currentIndex]) },
                                { "then", true },
                                { "else", currentIndex + 1 < comparisonValues.Count ? CreateOuterExpression(comparisonValues, currentIndex + 1) : false },
                            });
                        }

                        BsonDocument CreateArrayEqualityExpression(List<string> comparisonValues, int currentIndex = 0)
                        {
                            var valueGetter = new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                getter,
                                currentIndex
                            });

                            if (compareStringArray.StringsInvariant)
                            {
                                valueGetter = new BsonDocument("$toLower", valueGetter);
                            }

                            return new BsonDocument("$cond", new BsonDocument()
                            {
                                { "if", new BsonDocument("$ne", new BsonArray
                                    {
                                        new BsonDocument("$indexOfCP", new BsonArray
                                        {
                                            valueGetter,
                                            comparisonValues[currentIndex]
                                        }),
                                        -1
                                    }) },
                                { "then", currentIndex + 1 < comparisonValues.Count ? CreateArrayEqualityExpression(comparisonValues, currentIndex + 1) : true },
                                { "else", false },
                            });
                        }

                        condition = CreateOuterExpression(compareStringArray.ComparisonValues);
                    }
                    else
                    {
                        if (compareStringArray.StringsInvariant)
                        {
                            getter = new BsonDocument("$map", new BsonDocument()
                            {
                                { "input", getter },
                                { "in", new BsonDocument("$toLower", "$$this") }
                            });
                        }

                        condition = TranslateExpression(compareStringArray, getter);
                    }

                    break;
                }

                case DbFindCompareFieldValues<Vector> compareVector:
                {
                    condition = TranslateExpression(compareVector, getter);
                    break;
                }

                case DbFindCompareFieldValues<List<Vector>> compareVectorArray:
                {
                    condition = TranslateExpression(compareVectorArray, getter);
                    break;
                }

                case DbFindCompareFieldValues<Typename> compareTypename:
                {
                    getter = new BsonDocument("$cond", new BsonDocument()
                    {
                        { "if", new BsonDocument("$eq", new BsonArray
                            {
                                new BsonDocument("$type", getter),
                                "string"
                            })
                        },
                        { "then", getter },
                        { "else", new BsonDocument("$getField", new BsonDocument()
                        {
                            { "field", DbFindCondition.TypeDiscriminator },
                            { "input", getter },
                        })
                        },
                    });

                    condition = TranslateExpression(compareTypename, getter);
                    break;
                }

                case DbFindCompareFieldValues<List<Typename>> compareTypenameArray:
                {
                    getter = new BsonDocument("$cond", new BsonDocument()
                    {
                        { "if", new BsonDocument("$eq", new BsonArray
                            {
                                new BsonDocument("$type", new BsonDocument("$first", getter)),
                                "string"
                            })
                        },
                        { "then", getter },
                        { "else", new BsonDocument("$map", new BsonDocument()
                            {
                                { "input", getter },
                                { "in", new BsonDocument("$getField", new BsonDocument()
                                    {
                                        { "field", DbFindCondition.TypeDiscriminator },
                                        { "input", "$$this" },
                                    })
                                }
                            })
                        }
                    });

                    condition = TranslateExpression(compareTypenameArray, getter);
                    break;
                }

                default: throw new NotImplementedException();
            }
        }

        if (arrayMode)
        {
            if (terminatorSegment.CollectionIndices?.Count > 0)
            {
                input = new BsonArray(terminatorSegment.CollectionIndices.Select(x => new BsonDocument("$arrayElemAt", new BsonArray()
                {
                    input,
                    x
                })));
            }
            else if (terminatorSegment.CollectionTypeFilters?.Count > 0)
            {
                input = new BsonDocument("$filter", new BsonDocument()
                {
                    { "input", input },
                    { "cond", new BsonDocument("$in", new BsonArray()
                        {
                            $"$$this.{DbFindCondition.TypeDiscriminator}",
                            BsonArray.Create(terminatorSegment.CollectionTypeFilters)
                        })
                    },
                });
            }

            condition = new BsonDocument(anyMatch ? "$anyElementTrue" : "$allElementsTrue", new BsonDocument("$map", new BsonDocument
            {
                { "input", input },
                { "in", condition }
            }));
        }

        return condition;
    }

    protected BsonDocument TranslateExpression<ValueType>(DbFindCompareFieldValues<ValueType> condition, BsonValue getter)
    {
        return condition.ComparisonOperator switch
        {
            DbFindOperator.EQUAL => new BsonDocument("$in", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues)
            }),

            DbFindOperator.NOT_EQUAL => new BsonDocument("$not", new BsonDocument("$in", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues)
            })),

            DbFindOperator.LESS_THAN => new BsonDocument("$lt", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues.Min())
            }),

            DbFindOperator.LESS_THAN_OR_EQUAL => new BsonDocument("$lte", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues.Min())
            }),

            DbFindOperator.GREATER_THAN => new BsonDocument("$gt", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues.Max())
            }),

            DbFindOperator.GREATER_THAN_OR_EQUAL => new BsonDocument("$gte", new BsonArray
            {
                getter,
                BsonValue.Create(condition.ComparisonValues.Max())
            }),

            DbFindOperator.CONTAINS => new BsonDocument("$ne", new BsonArray
            {
                new BsonDocument("$setIntersection", new BsonArray
                {
                    BsonValue.Create(condition.ComparisonValues),
                    getter
                }),
                new BsonArray()
            }),

            DbFindOperator.NOT_CONTAINS => new BsonDocument("$eq", new BsonArray
            {
                new BsonDocument("$setIntersection", new BsonArray
                {
                    BsonValue.Create(condition.ComparisonValues),
                    getter
                }),
                new BsonArray()
            }),

            DbFindOperator.CONTAINS_ALL => new BsonDocument("$setIsSubset", new BsonArray
            {
                BsonValue.Create(condition.ComparisonValues),
                getter
            }),

            DbFindOperator.NOT_CONTAINS_ALL => new BsonDocument("$not", new BsonArray
            {
                new BsonDocument("$setIsSubset", new BsonArray
                {
                    BsonValue.Create(condition.ComparisonValues),
                    getter
                })
            }),

            _ => throw new NotImplementedException(),
        };
    }

    protected class TypenameBsonTypeMapper : ICustomBsonTypeMapper
    {
        public bool TryMapToBsonValue(object value, out BsonValue bsonValue)
        {
            bsonValue = BsonValue.Create(((Typename)value).ClassName);
            return true;
        }
    }
}
