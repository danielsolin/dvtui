using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Label = Microsoft.Xrm.Sdk.Label;
using LocalizedLabel = Microsoft.Xrm.Sdk.LocalizedLabel;
using RetrieveDependenciesForDeleteRequest =
    Microsoft.Crm.Sdk.Messages.RetrieveDependenciesForDeleteRequest;
using RetrieveDependenciesForDeleteResponse =
    Microsoft.Crm.Sdk.Messages.RetrieveDependenciesForDeleteResponse;

namespace dvtui.TerminalTests;

internal static class ServiceTests
{
    private static int _failures;

    public static int Run()
    {
        _failures = 0;
        TestManagedSolutionBlocksWrites();
        TestWriteContextLoadsPublisherAndLanguage();
        TestManagedWriteContextIsReadOnly();
        TestLoadColumnUsesBaseLanguage();
        TestCreateTableBuildsRequest();
        TestCreateColumnMapsKind();
        TestCreateColumnTreatsMissingNameAsAvailable();
        TestCreateColumnMapsSupportedKinds();
        TestCreateColumnRejectsInvalidLength();
        TestCreateColumnRejectsUnsupportedPrecision();
        TestUpdateColumnNoChangeSendsNothing();
        TestUpdateColumnConflictOnIdChange();
        TestUpdateColumnRejectsLengthDecrease();
        TestUpdateColumnClearsDescription();
        TestUpdatePreservesOtherLabels();
        TestDeleteColumnSendsNothingWhenManaged();
        TestDeleteColumnVerifiesIdentity();
        TestDeleteColumnBlocksDependencies();
        TestDeleteColumnVerifiesAbsence();
        TestPublishTableTargetsOneTable();
        TestCapabilityPolicyReadonlyKinds();
        TestCapabilityPolicySpecializedFormats();
        TestCapabilityPolicyManagedColumn();
        TestCapabilityPolicyPrimaryColumns();
        TestCapabilityPolicyPerPropertyFlags();
        TestCapabilityPolicyTableCreate();
        TestRequirementLevelMapping();
        return _failures;
    }

    private static void Check(bool condition, string name)
    {
        if( condition )
        {
            Console.WriteLine($"  PASS {name}");
        }
        else
        {
            Console.WriteLine($"  FAIL {name}");
            _failures++;
        }
    }

    private static SolutionWriteContext UnmanagedContext()
    {
        return new SolutionWriteContext
        {
            SolutionId = Guid.NewGuid(),
            SolutionUniqueName = "contoso",
            IsManaged = false,
            PublisherId = Guid.NewGuid(),
            PublisherPrefix = "new",
            BaseLanguage = "1033",
            EnvironmentUrl = "https://example.crm.dynamics.com"
        };
    }

    private static DataverseSolution UnmanagedSolution()
    {
        return new DataverseSolution
        {
            Id = Guid.NewGuid(),
            FriendlyName = "Contoso",
            UniqueName = "contoso",
            Version = "1.0.0.0",
            IsManaged = false,
            Description = "test"
        };
    }

    private static void TestManagedSolutionBlocksWrites()
    {
        var fake = new FakeExecutor();
        var service = new DataverseSchemaService(fake);
        var managed = new SolutionWriteContext
        {
            SolutionId = Guid.NewGuid(),
            SolutionUniqueName = "managed",
            PublisherId = Guid.NewGuid(),
            PublisherPrefix = "new",
            BaseLanguage = "1033",
            IsManaged = true
        };

        var threw = false;
        try
        {
            service.CreateTableAsync(
                new CreateTableRequest
                {
                    Context = managed,
                    DisplayName = "T",
                    PluralDisplayName = "Ts",
                    SchemaSuffix = "t",
                    PrimaryNameDisplayName = "Name",
                    PrimaryNameSchemaSuffix = "name"
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException )
        {
            threw = true;
        }

        Check(threw, "managed solution blocks create table");
        Check(fake.ExecutedRequests.Count == 0, "managed create sends no request");
    }

    private static void TestWriteContextLoadsPublisherAndLanguage()
    {
        var fake = new FakeExecutor();
        var solution = UnmanagedSolution();
        var publisherId = Guid.NewGuid();
        fake.OnRetrieve = query =>
        {
            var qe = (QueryExpression)query;
            var collection = new EntityCollection();
            if( qe.EntityName == "publisher" )
            {
                var entity = new Entity("publisher");
                entity["publisherid"] = publisherId;
                entity["customizationprefix"] = "new";
                collection.Entities.Add(entity);
            }
            else if( qe.EntityName == "organization" )
            {
                var entity = new Entity("organization");
                entity["languagecode"] = 1033;
                collection.Entities.Add(entity);
            }

            return collection;
        };

        var service = new DataverseSchemaService(fake);
        var context = service.LoadWriteContextAsync(
            solution,
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(context.PublisherPrefix == "new", "context publisher prefix");
        Check(context.BaseLanguage == "1033", "context base language");
        Check(context.PublisherId == publisherId, "context publisher id");
        Check(context.SolutionUniqueName == "contoso", "context unique name");
    }

    private static void TestManagedWriteContextIsReadOnly()
    {
        var fake = new FakeExecutor();
        var baseSolution = UnmanagedSolution();
        var solution = new DataverseSolution
        {
            Id = baseSolution.Id,
            FriendlyName = baseSolution.FriendlyName,
            UniqueName = baseSolution.UniqueName,
            Version = baseSolution.Version,
            IsManaged = true,
            Description = baseSolution.Description,
            PublisherId = baseSolution.PublisherId
        };
        var service = new DataverseSchemaService(fake);
        var context = service.LoadWriteContextAsync(
            solution,
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(context.IsManaged, "managed context keeps managed state");
        Check(!context.CanWrite, "managed context disables writes");
        Check(
            context.WriteDisabledReason
                == ColumnCapabilityPolicy.ReasonManagedSolution,
            "managed context explains disabled writes"
        );
        Check(fake.Queries.Count == 0, "managed context does not read publisher");
    }

    private static void TestLoadColumnUsesBaseLanguage()
    {
        var fake = new FakeExecutor();
        var column = CustomColumn(Guid.NewGuid());
        column.DisplayName!.LocalizedLabels.Add(new LocalizedLabel
        {
            Label = "Anteckning",
            LanguageCode = 1053
        });
        fake.OnExecute = request =>
        {
            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = column;
            return response;
        };

        var result = new DataverseSchemaService(fake)
            .LoadColumnDefinitionAsync(
                "new_things",
                "new_note",
                retrieveAsIfPublished: false,
                CancellationToken.None,
                "1033"
            )
            .GetAwaiter().GetResult();

        Check(result.DisplayName == "Note", "base-language display label");
        Check(
            result.Description == "A note",
            "base-language description label"
        );
    }

    private static void TestCreateTableBuildsRequest()
    {
        var fake = new FakeExecutor();
        var entityId = Guid.NewGuid();
        fake.OnExecute = request =>
        {
            if( request is CreateEntityRequest create )
            {
                Check(
                    create.Entity.LogicalName == "new_things",
                    "create table logical name"
                );
                Check(
                    create.PrimaryAttribute is StringAttributeMetadata
                        && create.PrimaryAttribute.SchemaName == "new_name",
                    "create table primary attribute"
                );
                Check(
                    create.SolutionUniqueName == "contoso",
                    "create table solution unique name"
                );
            }

            var response = new CreateEntityResponse();
            response.Results["EntityId"] = entityId;
            return response;
        };

        var service = new DataverseSchemaService(fake);
        var result = service.CreateTableAsync(
            new CreateTableRequest
            {
                Context = UnmanagedContext(),
                DisplayName = "Thing",
                PluralDisplayName = "Things",
                SchemaSuffix = "things",
                PrimaryNameDisplayName = "Name",
                PrimaryNameSchemaSuffix = "name",
                PrimaryNameMaxLength = 100
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(result == entityId, "create table returns entity id");
    }

    private static void TestCreateColumnMapsKind()
    {
        var fake = new FakeExecutor();
        var attributeId = Guid.NewGuid();
        fake.OnExecute = request =>
        {
            if( request is CreateAttributeRequest create )
            {
                Check(
                    create.Attribute is StringAttributeMetadata
                        && create.Attribute.SchemaName == "new_note"
                        && ((StringAttributeMetadata)create.Attribute).Format
                            == StringFormat.Text
                        && ((StringAttributeMetadata)create.Attribute).FormatName
                            == StringFormatName.Text,
                    "create column text maps to string metadata"
                );
            }

            var response = new CreateAttributeResponse();
            response.Results["AttributeId"] = attributeId;
            return response;
        };

        var service = new DataverseSchemaService(fake);
        var result = service.CreateColumnAsync(
            new CreateColumnRequest
            {
                Context = UnmanagedContext(),
                TableLogicalName = "new_things",
                DisplayName = "Note",
                SchemaSuffix = "note",
                Kind = ColumnKind.Text,
                MaxLength = 200
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(result == attributeId, "create column returns attribute id");
    }

    private static void TestCreateColumnMapsSupportedKinds()
    {
        Check(
            CreateColumnAndCheck(
                ColumnKind.MultilineText,
                request => request.Attribute is MemoAttributeMetadata
                    && ((MemoAttributeMetadata)request.Attribute).MaxLength == 2000
                    && ((MemoAttributeMetadata)request.Attribute).Format
                        == StringFormat.TextArea
                    && ((MemoAttributeMetadata)request.Attribute).FormatName
                        == MemoFormatName.TextArea
            ),
            "create column memo maps settings"
        );
        Check(
            CreateColumnAndCheck(
                ColumnKind.WholeNumber,
                request => request.Attribute is IntegerAttributeMetadata integer
                    && integer.Format == IntegerFormat.None
                    && integer.MinValue == -10
                    && integer.MaxValue == 20
            ),
            "create column whole number maps bounds"
        );
        Check(
            CreateColumnAndCheck(
                ColumnKind.Decimal,
                request => request.Attribute is DecimalAttributeMetadata decimalValue
                    && decimalValue.MinValue == 1.25m
                    && decimalValue.MaxValue == 99.75m
                    && decimalValue.Precision == 3
            ),
            "create column decimal maps settings"
        );
        Check(
            CreateColumnAndCheck(
                ColumnKind.YesNo,
                request => request.Attribute is BooleanAttributeMetadata booleanValue
                    && booleanValue.DefaultValue == true
                    && booleanValue.OptionSet?.TrueOption?.Label
                        ?.UserLocalizedLabel?.Label == "Enabled"
                    && booleanValue.OptionSet?.FalseOption?.Label
                        ?.UserLocalizedLabel?.Label == "Disabled"
            ),
            "create column yes-no maps settings"
        );
    }

    private static void TestCreateColumnTreatsMissingNameAsAvailable()
    {
        var fake = new FakeExecutor();
        var solutionId = Guid.NewGuid();
        var publisherId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var table = new EntityMetadata
        {
            MetadataId = tableId,
            LogicalName = "new_things",
            IsCustomizable = new BooleanManagedProperty(true),
            CanCreateAttributes = new BooleanManagedProperty(true)
        };
        var column = new StringAttributeMetadata
        {
            MetadataId = columnId,
            LogicalName = "new_note",
            SchemaName = "new_note"
        };
        fake.OnRetrieve = query =>
        {
            var queryExpression = (QueryExpression)query;
            var response = new EntityCollection();
            if( queryExpression.EntityName == "solution" )
            {
                var solution = new Entity("solution");
                solution["solutionid"] = solutionId;
                solution["uniquename"] = "contoso";
                solution["ismanaged"] = false;
                solution["publisherid"] = publisherId;
                response.Entities.Add(solution);
            }
            else if( queryExpression.EntityName == "solutioncomponent" )
            {
                var component = new Entity("solutioncomponent");
                component["solutionid"] = solutionId;
                component["componenttype"] = new OptionSetValue(1);
                component["objectid"] = tableId;
                response.Entities.Add(component);
            }

            return response;
        };
        fake.OnExecute = request =>
        {
            if( request is RetrieveEntityRequest )
            {
                var response = new RetrieveEntityResponse();
                response.Results["EntityMetadata"] = table;
                return response;
            }

            if( request is RetrieveAttributeRequest retrieve )
            {
                if( retrieve.MetadataId == Guid.Empty )
                {
                    throw new InvalidOperationException(
                        "Could not find an attribute () with name new_note "
                            + "and id 00000000-0000-0000-0000-000000000000 "
                            + "and columnNumber 0 in new_things."
                    );
                }

                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            if( request is CreateAttributeRequest create )
            {
                Check(
                    create.Attribute.LogicalName == "new_note",
                    "create column sends the predicted logical name"
                );
                var response = new CreateAttributeResponse();
                response.Results["AttributeId"] = columnId;
                return response;
            }

            throw new InvalidOperationException(
                "Unexpected request " + request.GetType().Name
            );
        };

        var context = new SolutionWriteContext
        {
            SolutionId = solutionId,
            SolutionUniqueName = "contoso",
            PublisherId = publisherId,
            PublisherPrefix = "new",
            BaseLanguage = "1033",
            EnvironmentUrl = "https://example.crm.dynamics.com"
        };
        var result = new DataverseSchemaService(
            fake,
            context.EnvironmentUrl
        ).CreateColumnAsync(
            new CreateColumnRequest
            {
                Context = context,
                TableLogicalName = "new_things",
                TableMetadataId = tableId,
                DisplayName = "Note",
                SchemaSuffix = "note",
                Kind = ColumnKind.Text,
                MaxLength = 100
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(result == columnId, "missing column probe allows create");
        Check(
            fake.ExecutedRequests.OfType<CreateAttributeRequest>().Count() == 1,
            "missing column probe does not suppress create"
        );
    }

    private static bool CreateColumnAndCheck(
        ColumnKind kind,
        Func<CreateAttributeRequest, bool> check
    )
    {
        var fake = new FakeExecutor();
        var matched = false;
        fake.OnExecute = request =>
        {
            if( request is CreateAttributeRequest create )
            {
                matched = check(create);
            }

            var response = new CreateAttributeResponse();
            response.Results["AttributeId"] = Guid.NewGuid();
            return response;
        };
        var service = new DataverseSchemaService(fake);
        var request = new CreateColumnRequest
        {
            Context = UnmanagedContext(),
            TableLogicalName = "new_things",
            DisplayName = "Field",
            SchemaSuffix = "field",
            Kind = kind,
            MaxLength = kind == ColumnKind.MultilineText ? 2000 : null,
            MinValue = kind == ColumnKind.WholeNumber
                ? -10
                : kind == ColumnKind.Decimal ? 1.25m : null,
            MaxValue = kind == ColumnKind.WholeNumber
                ? 20
                : kind == ColumnKind.Decimal ? 99.75m : null,
            Precision = kind == ColumnKind.Decimal ? 3 : null,
            BooleanDefaultValue = kind == ColumnKind.YesNo,
            BooleanTrueLabel = "Enabled",
            BooleanFalseLabel = "Disabled"
        };
        service.CreateColumnAsync(
            request,
            CancellationToken.None
        ).GetAwaiter().GetResult();
        return matched;
    }

    private static void TestCreateColumnRejectsInvalidLength()
    {
        var fake = new FakeExecutor();
        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.CreateColumnAsync(
                new CreateColumnRequest
                {
                    Context = UnmanagedContext(),
                    TableLogicalName = "new_things",
                    DisplayName = "Note",
                    SchemaSuffix = "note",
                    Kind = ColumnKind.Text,
                    MaxLength = 999999
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException )
        {
            threw = true;
        }

        Check(threw, "create column rejects oversized text length");
        Check(fake.ExecutedRequests.Count == 0, "invalid create sends no request");
    }

    private static void TestCreateColumnRejectsUnsupportedPrecision()
    {
        var fake = new FakeExecutor();
        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.CreateColumnAsync(
                new CreateColumnRequest
                {
                    Context = UnmanagedContext(),
                    TableLogicalName = "new_things",
                    DisplayName = "Amount",
                    SchemaSuffix = "amount",
                    Kind = ColumnKind.Decimal,
                    Precision = ColumnDefaults.DecimalMaxPrecision + 1
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException )
        {
            threw = true;
        }

        Check(threw, "create column rejects unsupported precision");
        Check(fake.ExecutedRequests.Count == 0, "invalid precision sends no request");
    }

    private static void TestUpdateColumnNoChangeSendsNothing()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest )
            {
                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            throw new InvalidOperationException("unexpected " + request.GetType().Name);
        };

        var service = new DataverseSchemaService(fake);
        var result = service.UpdateColumnAsync(
            new UpdateColumnRequest
            {
                Context = UnmanagedContext(),
                TableLogicalName = "new_things",
                ColumnLogicalName = "new_note",
                ExpectedMetadataId = metadataId,
                SetDisplayName = true,
                DisplayName = "Note",
                SetDescription = true,
                Description = "A note",
                NewMaxLength = 100,
                SetRequirementLevel = true,
                RequirementLevel = RequirementLevels.Optional
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(!result.Changed, "no-change update reports unchanged");
        Check(
            fake.ExecutedRequests.Count == 1,
            "no-change update sends only the read"
        );
    }

    private static void TestUpdateColumnConflictOnIdChange()
    {
        var fake = new FakeExecutor();
        var expected = Guid.NewGuid();
        var column = CustomColumn(Guid.NewGuid());
        fake.OnExecute = request =>
        {
            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = column;
            return response;
        };

        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.UpdateColumnAsync(
                new UpdateColumnRequest
                {
                    Context = UnmanagedContext(),
                    TableLogicalName = "new_things",
                    ColumnLogicalName = "new_note",
                    ExpectedMetadataId = expected,
                    SetDisplayName = true,
                    DisplayName = "Changed"
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( ColumnConflictException )
        {
            threw = true;
        }

        Check(threw, "update detects metadata id conflict");
        Check(
            fake.ExecutedRequests.Count == 1,
            "conflict update sends no write"
        );
    }

    private static void TestUpdateColumnRejectsLengthDecrease()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest )
            {
                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            throw new InvalidOperationException("unexpected " + request.GetType().Name);
        };

        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.UpdateColumnAsync(
                new UpdateColumnRequest
                {
                    Context = UnmanagedContext(),
                    TableLogicalName = "new_things",
                    ColumnLogicalName = "new_note",
                    ExpectedMetadataId = metadataId,
                    NewMaxLength = 10
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException )
        {
            threw = true;
        }

        Check(threw, "update rejects length decrease");
        Check(
            fake.ExecutedRequests.Count == 1,
            "decrease sends no write"
        );
    }

    private static void TestUpdateColumnClearsDescription()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        column.Description!.LocalizedLabels.Add(new LocalizedLabel
        {
            Label = "Beskrivning",
            LanguageCode = 1053
        });
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest )
            {
                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            if( request is UpdateAttributeRequest update )
            {
                Check(
                    update.Attribute.Description != null
                        && update.Attribute.Description.LocalizedLabels.Any(
                            label => label.LanguageCode == 1033
                                && label.Label == string.Empty
                        ),
                    "cleared description updates the base language"
                );
                Check(
                    update.Attribute.Description != null
                        && update.Attribute.Description.LocalizedLabels.Any(
                            label => label.LanguageCode == 1053
                                && label.Label == "Beskrivning"
                        ),
                    "clearing description preserves other languages"
                );
                Check(
                    update.MergeLabels,
                    "update uses merge labels"
                );
            }

            return new UpdateAttributeResponse();
        };

        var service = new DataverseSchemaService(fake);
        service.UpdateColumnAsync(
            new UpdateColumnRequest
            {
                Context = UnmanagedContext(),
                TableLogicalName = "new_things",
                ColumnLogicalName = "new_note",
                ExpectedMetadataId = metadataId,
                SetDescription = true,
                Description = string.Empty
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(
            fake.ExecutedRequests.Count == 2,
            "clear description sends read and write"
        );
    }

    private static void TestUpdatePreservesOtherLabels()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        column.DisplayName!.LocalizedLabels.Add(new LocalizedLabel
        {
            Label = "Anteckning",
            LanguageCode = 1053
        });
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest )
            {
                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            if( request is UpdateAttributeRequest update )
            {
                var labels = update.Attribute.DisplayName.LocalizedLabels;
                Check(
                    labels.Any(label => label.LanguageCode == 1033
                        && label.Label == "Changed"),
                    "update changes base-language label"
                );
                Check(
                    labels.Any(label => label.LanguageCode == 1053
                        && label.Label == "Anteckning"),
                    "update preserves other-language label"
                );
            }

            return new UpdateAttributeResponse();
        };

        var service = new DataverseSchemaService(fake);
        service.UpdateColumnAsync(
            new UpdateColumnRequest
            {
                Context = UnmanagedContext(),
                TableLogicalName = "new_things",
                ColumnLogicalName = "new_note",
                ExpectedMetadataId = metadataId,
                SetDisplayName = true,
                DisplayName = "Changed"
            },
            CancellationToken.None
        ).GetAwaiter().GetResult();
    }

    private static void TestDeleteColumnSendsNothingWhenManaged()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var managed = new StringAttributeMetadata
        {
            MetadataId = metadataId,
            LogicalName = "new_note",
            SchemaName = "new_note",
            IsCustomizable = new BooleanManagedProperty(true),
            IsRenameable = new BooleanManagedProperty(true),
            CanModifyAdditionalSettings = new BooleanManagedProperty(true),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.None
            )
        };
        fake.OnExecute = request =>
        {
            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = managed;
            return response;
        };

        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.DeleteColumnAsync(
                "new_things",
                "new_note",
                metadataId,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException )
        {
            threw = true;
        }

        Check(threw, "delete managed column is rejected");
        Check(
            fake.ExecutedRequests.Count == 1,
            "managed delete sends no delete request"
        );
    }

    private static void TestDeleteColumnVerifiesIdentity()
    {
        var fake = new FakeExecutor();
        var expected = Guid.NewGuid();
        var column = CustomColumn(Guid.NewGuid());
        fake.OnExecute = request =>
        {
            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = column;
            return response;
        };

        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.DeleteColumnAsync(
                "new_things",
                "new_note",
                expected,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( ColumnConflictException )
        {
            threw = true;
        }

        Check(threw, "delete detects identity change");
        Check(
            fake.ExecutedRequests.Count == 1,
            "identity mismatch sends no delete"
        );
    }

    private static void TestDeleteColumnBlocksDependencies()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest retrieve )
            {
                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            if( request is RetrieveDependenciesForDeleteRequest )
            {
                var dependency = new Entity("dependency");
                dependency["dependentcomponenttype"] = new OptionSetValue(60);
                dependency["dependentcomponentobjectid"] = Guid.NewGuid();
                dependency["dependentcomponentname"] = "A form";
                var response = new RetrieveDependenciesForDeleteResponse();
                response.Results["EntityCollection"] = new EntityCollection(
                    new[] { dependency }
                );
                return response;
            }

            throw new InvalidOperationException(
                "Delete must not be sent when dependencies exist."
            );
        };

        var service = new DataverseSchemaService(fake);
        var threw = false;
        try
        {
            service.DeleteColumnAsync(
                "new_things",
                "new_note",
                metadataId,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch( InvalidOperationException ex )
        {
            threw = ex.Message.Contains("dependencies");
        }

        Check(threw, "delete blocks dependent column");
        Check(
            fake.ExecutedRequests.Count == 2,
            "dependency check sends no delete request"
        );
    }

    private static void TestDeleteColumnVerifiesAbsence()
    {
        var fake = new FakeExecutor();
        var metadataId = Guid.NewGuid();
        var column = CustomColumn(metadataId);
        var retrieveCount = 0;
        fake.OnExecute = request =>
        {
            if( request is RetrieveAttributeRequest )
            {
                retrieveCount++;
                if( retrieveCount > 1 )
                {
                    throw new InvalidOperationException(
                        "Could not find an attribute () with name new_note "
                            + "and id 00000000-0000-0000-0000-000000000000 "
                            + "and columnNumber 0 in new_things."
                    );
                }

                var response = new RetrieveAttributeResponse();
                response.Results["AttributeMetadata"] = column;
                return response;
            }

            if( request is RetrieveDependenciesForDeleteRequest )
            {
                var response = new RetrieveDependenciesForDeleteResponse();
                response.Results["EntityCollection"] = new EntityCollection();
                return response;
            }

            if( request is DeleteAttributeRequest )
            {
                return new OrganizationResponse();
            }

            throw new InvalidOperationException(
                "Unexpected request " + request.GetType().Name
            );
        };

        var service = new DataverseSchemaService(fake);
        service.DeleteColumnAsync(
            "new_things",
            "new_note",
            metadataId,
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(
            fake.ExecutedRequests.Count == 4,
            "delete verifies absence after dispatch"
        );
    }

    private static void TestPublishTableTargetsOneTable()
    {
        var fake = new FakeExecutor();
        fake.OnExecute = request =>
        {
            if( request is Microsoft.Crm.Sdk.Messages.PublishXmlRequest publish )
            {
                Check(
                    publish.ParameterXml.Contains("new_things"),
                    "publish xml targets table"
                );
                Check(
                    !publish.ParameterXml.Contains("PublishAll"),
                    "publish is not publish-all"
                );
                Check(
                    publish.ParameterXml.Contains(
                        "<entities><entity>new_things</entity></entities>"
                    ),
                    "publish uses one-table xml"
                );
            }

            return new OrganizationResponse();
        };

        var service = new DataverseSchemaService(fake);
        service.PublishTableAsync(
            "new_things",
            CancellationToken.None
        ).GetAwaiter().GetResult();

        Check(fake.ExecutedRequests.Count == 1, "publish sends one request");
    }

    private static void TestCapabilityPolicyReadonlyKinds()
    {
        var lookup = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "parentid",
            SchemaName = "parentid",
            Kind = ColumnKind.Unknown,
            AttributeTypeCode = "Lookup",
            IsCustom = false,
            IsManaged = false,
            IsPrimaryName = false
        };
        var capability = ColumnCapabilityPolicy.Evaluate(lookup);
        Check(!capability.CanEdit, "lookup column is read-only");
        Check(!capability.CanDelete, "lookup column not deletable");
    }

    private static void TestCapabilityPolicyManagedColumn()
    {
        var managed = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "new_note",
            SchemaName = "new_note",
            Kind = ColumnKind.Text,
            IsCustom = true,
            IsPrimaryName = false
        };
        var capability = ColumnCapabilityPolicy.Evaluate(managed);
        Check(!capability.CanEdit, "managed column is read-only");
        Check(!capability.CanDelete, "managed column not deletable");
    }

    private static void TestCapabilityPolicySpecializedFormats()
    {
        var email = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "new_email",
            SchemaName = "new_email",
            Kind = ColumnKind.Text,
            AttributeFormat = "Email",
            IsCustom = true,
            IsManaged = false,
            IsCustomizable = true,
            IsRenameable = true,
            CanModifyAdditionalSettings = true,
            CanChangeRequirement = true
        };
        var capability = ColumnCapabilityPolicy.Evaluate(email);
        Check(!capability.CanEdit, "specialized text format is read-only");
        Check(!capability.CanDelete, "specialized text format cannot be deleted");
    }

    private static void TestCapabilityPolicyPrimaryColumns()
    {
        var primaryId = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "id",
            SchemaName = "id",
            Kind = ColumnKind.Unknown,
            IsPrimaryId = true
        };
        Check(
            !ColumnCapabilityPolicy.Evaluate(primaryId).CanEdit,
            "primary id column is read-only"
        );

        var primaryName = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "new_name",
            SchemaName = "new_name",
            Kind = ColumnKind.Text,
            IsPrimaryName = true
        };
        Check(
            !ColumnCapabilityPolicy.Evaluate(primaryName).CanEdit,
            "primary name column is read-only"
        );
    }

    private static void TestCapabilityPolicyPerPropertyFlags()
    {
        var noRename = new DataverseColumn
        {
            MetadataId = Guid.NewGuid(),
            LogicalName = "new_note",
            SchemaName = "new_note",
            Kind = ColumnKind.Text,
            IsCustom = true,
            IsManaged = false,
            IsCustomizable = true,
            IsPrimaryId = false,
            IsPrimaryName = false,
            IsRenameable = false,
            CanModifyAdditionalSettings = true,
            CanChangeRequirement = true
        };
        var capability = ColumnCapabilityPolicy.Evaluate(noRename);
        Check(!capability.CanEditDisplayName, "non-renameable cannot rename");
        Check(capability.CanEditDescription, "settings customizable allows description");
        Check(capability.CanEditRequirement, "settings customizable allows requirement");
    }

    private static void TestCapabilityPolicyTableCreate()
    {
        var allowed = ColumnCapabilityPolicy.EvaluateTable(true, true);
        Check(allowed.CanCreateColumn, "customizable table allows columns");

        var blocked = ColumnCapabilityPolicy.EvaluateTable(true, false);
        Check(!blocked.CanCreateColumn, "cannot-create-attributes blocks columns");
    }

    private static void TestRequirementLevelMapping()
    {
        Check(
            RequirementLevels.GetDisplayName(RequirementLevels.Optional)
                == "Optional",
            "optional label"
        );
        Check(
            RequirementLevels.GetDisplayName(RequirementLevels.Required)
                == "Business required",
            "required label"
        );
        Check(
            RequirementLevels.GetDisplayName(RequirementLevels.Recommended)
                == "Business recommended",
            "recommended label"
        );
    }

    private static StringAttributeMetadata CustomColumn(Guid metadataId)
    {
        var column = new StringAttributeMetadata
        {
            MetadataId = metadataId,
            LogicalName = "new_note",
            SchemaName = "new_note",
            DisplayName = new Label
            {
                UserLocalizedLabel = new LocalizedLabel
                {
                    Label = "Note",
                    LanguageCode = 1033
                }
            },
            Description = new Label
            {
                UserLocalizedLabel = new LocalizedLabel
                {
                    Label = "A note",
                    LanguageCode = 1033
                }
            },
            MaxLength = 100,
            IsCustomizable = new BooleanManagedProperty(true),
            IsRenameable = new BooleanManagedProperty(true),
            CanModifyAdditionalSettings = new BooleanManagedProperty(true),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.None
            )
        };
        SetReadOnly(column, "IsCustomAttribute", true);
        SetReadOnly(column, "IsManaged", false);
        SetReadOnly(column, "IsPrimaryId", false);
        SetReadOnly(column, "IsPrimaryName", false);
        return column;
    }

    private static void SetReadOnly(
        object target,
        string name,
        object value
    )
    {
        var type = target.GetType();
        while( type != null )
        {
            var field = type
                .GetField(name, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
            if( field != null )
            {
                field.SetValue(target, value);
                return;
            }

            var backing = type
                .GetField("_" + name, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
            if( backing != null )
            {
                backing.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        var amType = typeof(Microsoft.Xrm.Sdk.Metadata.AttributeMetadata);
        var fields = amType
            .GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
            );
        var backingName = "_" + char.ToLowerInvariant(name[0])
            + name.Substring(1);
        var direct = fields
            .FirstOrDefault(f => f.Name == backingName);
        direct?.SetValue(target, value);
    }
}
