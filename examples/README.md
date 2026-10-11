# Examples

[`ModularMonolith`](ModularMonolith) is a small shop built the way real applications use Pheme. Every feature
of Pheme has a use case here with an end-to-end test over HTTP on PostgreSQL, in
[`tests/Pheme.Examples.IntegrationTests`](../tests/Pheme.Examples.IntegrationTests); the layer and registration tests,
which need no database, in [`tests/Pheme.Examples.UnitTests`](../tests/Pheme.Examples.UnitTests).

| Project | Role |
|---|---|
| `Shop.Api` | composition root: controllers, `AddPheme` with the pipeline, the outbox and the contracts, behaviors, exception filter, authentication (`Security`) |
| `Shop.Common` | shared kernel: application exceptions (`Errors`), contracts between the modules (`Contracts`), the current user and roles (`Security`), cacheable queries (`Caching`), schema creation (`Persistence`) |
| `Ordering.Domain`, `Ordering.Application`, `Ordering.Infrastructure` | ordering module, one assembly per layer, feature folders inside (`Orders/PlaceOrder`, `Inventory`, ...) |
| `Shipping` | shipping module, one assembly with the layers as folders `Domain`, `Application`, `Infrastructure` |

## Running

From the repository root. The tests start PostgreSQL in a container (Testcontainers), so they need Docker running:

```sh
dotnet test --project tests/Pheme.Examples.IntegrationTests
```

The layer and registration tests need no database: `dotnet test --project tests/Pheme.Examples.UnitTests`.

The application needs a PostgreSQL database in `ConnectionStrings__Shop`; it creates the schemas at startup:

```sh
docker run --rm -d --name shop-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:17-alpine
ConnectionStrings__Shop="Host=localhost;Database=shop;Username=postgres;Password=postgres" \
    dotnet run --project examples/ModularMonolith/Shop.Api
```

With `OTEL_EXPORTER_OTLP_ENDPOINT` set (for example `http://localhost:4317` of the Aspire dashboard) it exports its
traces and metrics. The outbox processor polls every 5 seconds by default; the configuration section `Outbox` sets its options, for
example `Outbox__PollingInterval=00:00:01`. `GET /admin/outbox/parked` lists the parked messages,
`POST /admin/outbox/{id}/requeue` requeues one; `GET /health/outbox` reports the outbox, for monitoring. The warehouse
endpoints, `POST /shipments/{orderId}/dispatch` and `POST /stocks/{product}/receive`, need a user in the warehouse role:
the headers `X-User: clerk` and `X-Roles: warehouse`.

## Use cases

Paths below are relative to `ModularMonolith`; tests are in `tests/Pheme.Examples.IntegrationTests`, the layer and
registration tests in `tests/Pheme.Examples.UnitTests`. Folders are features and namespaces follow them
(`Ordering.Application/Orders/PlaceOrder` is `Ordering.Application.Orders.PlaceOrder`).

### Commands, queries and validation

| Use case | Code | Tests |
|---|---|---|
| A command with a response: `POST /orders` answers 201 with the id | `Ordering.Application/Orders/PlaceOrder/`, `Shop.Api/Controllers/OrdersController.cs` | `OrderTests.Placing_an_order_answers_its_identifier_and_location` |
| FluentValidation failures as the application's own exception: 400 with the errors per field | `Ordering.Application/Orders/PlaceOrder/PlaceOrderCommandValidator.cs`, `AddFluentValidation` in `Shop.Api/Program.cs`, `Shop.Api/Errors/ApiExceptionFilter.cs` | `OrderTests.Placing_an_invalid_order_answers_400_with_the_validators_errors_per_field_and_saves_nothing`, `OrderTests.Placing_an_order_without_a_product_is_rejected_by_the_validator_not_by_MVC` |
| A command without a response: `POST /orders/{id}/cancel` answers 204 | `Ordering.Application/Orders/CancelOrder/` | `OrderTests.Cancelling_an_order_answers_204_and_the_order_reads_cancelled`, `OrderTests.Cancelling_a_missing_order_answers_404` |
| A query: `GET /orders/{id}` answers 200, a handler's exception reaches the API unwrapped (404) | `Ordering.Application/Orders/GetOrder/`, `Shop.Common/Errors/NotFoundException.cs` | `OrderTests.Getting_a_missing_order_answers_404_with_the_handlers_message` |
| Each module registers its own handlers, `internal` ones too, from its infrastructure; the composition root adds the pipeline | `AddPheme` in `Ordering.Infrastructure/OrderingServiceCollectionExtensions.cs`, `Shipping/Infrastructure/ShippingServiceCollectionExtensions.cs` and `Shop.Api/Program.cs` | every test above |

### Pipeline

| Use case | Code | Tests |
|---|---|---|
| Request logging with validation, not found and business rule failures as expected outcomes | `AddRequestLogging` in `Shop.Api/Program.cs` | `PipelineTests.Validation_and_not_found_failures_are_logged_as_expected_without_a_stack_trace`, `PipelineTests.Successes_are_not_logged_at_the_default_debug_level` |
| An application behavior for commands only: a `Server-Timing` header | `Shop.Api/Behaviors/CommandTiming*.cs` | `PipelineTests.A_command_with_a_response_is_timed`, `PipelineTests.A_command_without_a_response_is_timed`, `PipelineTests.A_query_is_not_timed` |
| A transaction behavior for the commands that ask for one: receiving stock (`POST /stocks/{product}/receive`) closes the restock requests with `ExecuteDelete`, rolled back when the save conflicts (409) | `Ordering.Application/Behaviors/`, `Ordering.Application/Inventory/ReceiveStock/`, `Shop.Api/Controllers/StocksController.cs` | `TransactionTests.Receiving_stock_raises_it_and_closes_its_restock_requests`, `TransactionTests.A_receipt_whose_save_conflicts_answers_409_and_its_direct_delete_is_rolled_back` |
| An authorization behavior: the warehouse commands require a role named in their type (`IRequireRole`), refused with 403 before validation; the user from request headers, a stand-in for real authentication | `Shop.Api/Behaviors/AuthorizationBehavior.cs`, `Shop.Common/Security/`, `Shop.Api/Security/`, `Shop.Api/Errors/ApiExceptionFilter.cs` | `AuthorizationTests.Dispatching_a_shipment_outside_the_warehouse_staff_answers_403_and_dispatches_nothing`, `AuthorizationTests.A_message_outside_the_users_roles_is_refused_before_it_is_validated` |
| A query caching behavior on `HybridCache`: the stock of a product (`GET /stocks/{product}`, `ICachedQuery`) served from the cache for 30 seconds; failures and other queries not cached | `Shop.Api/Behaviors/QueryCachingBehavior.cs`, `Shop.Common/Caching/ICachedQuery.cs`, `Ordering.Application/Inventory/GetStock/` | `QueryCachingTests` |
| An idempotency behavior: an order sent again with its `Idempotency-Key` header answers the first identifier; of two at once, the later answers 409 and places nothing | `Ordering.Application/Behaviors/IdempotencyBehavior.cs`, `IIdempotentCommand.cs`, `ProcessedCommand.cs`, `OrdersController.Place` in `Shop.Api/Controllers/OrdersController.cs` | `IdempotencyTests` |
| An audit behavior: the ordering module's commands (`IAuditedCommand`) recorded with their user and content, saved with their change or not at all | `Ordering.Application/Behaviors/AuditBehavior.cs`, `IAuditedCommand.cs`, `AuditEntry.cs` | `AuditTests` |

### Domain events

Events are published by the `Pheme.EntityFrameworkCore` interceptor before `SaveChangesAsync` writes anything, so the
handlers' changes are saved in the same transaction. Each module registers it in its infrastructure with
`AddPhemeDbContext<TContext>` for its own `DbContext` and its own entity contract (`CollectDomainEventsFrom`):
`IHasDomainEvents` in ordering (registered after `AddDbContext`), the `AggregateRoot` base class in shipping (before
it). The `DbContext`s are plain `DbContext`s.

| Use case | Code | Tests |
|---|---|---|
| Events published on save, in the same transaction | `Ordering.Infrastructure/OrderingServiceCollectionExtensions.cs`, `Shipping/Infrastructure/ShippingServiceCollectionExtensions.cs`, `Ordering.Domain/IHasDomainEvents.cs`, `Ordering.Domain/Orders/Order.cs` | every test below |
| Handlers of one event, registered by hand to run in a set order | `Ordering.Application/OrderingApplicationServiceCollectionExtensions.cs`, `Ordering.Application/Orders/History/RecordOrderHistoryDomainEventHandler.cs`, `Ordering.Application/Inventory/AllocateStockDomainEventHandler.cs` | `DomainEventTests.Handlers_of_an_event_run_in_the_order_of_their_registrations_not_of_their_names`, `DomainEventTests.The_scan_alone_would_run_the_handlers_the_other_way_round` |
| Events of an `AggregateRoot` base class, published by the shipping module's own `DbContext`; the handler's change saved with the entity | `Shipping/Domain/AggregateRoot.cs`, `Shipping/Domain/Shipment.cs`, `Shipping/Application/NotifyWarehouse/NotifyWarehouseDomainEventHandler.cs`, `Shipping/Infrastructure/ShippingServiceCollectionExtensions.cs` | `DomainEventTests.An_event_of_the_shipping_module_is_published_when_its_own_DbContext_saves` |
| One class handling several events | `Ordering.Application/Orders/History/RecordOrderHistoryDomainEventHandler.cs`, `Shipping/Application/NotifyWarehouse/NotifyWarehouseDomainEventHandler.cs` | `DomainEventTests.One_handler_class_handles_both_placing_and_cancelling_an_order` |
| A handler's change raises a further event, published before the same save: the stock reaching its reorder level requests a restock, saved with the order | `Ordering.Application/Inventory/AllocateStockDomainEventHandler.cs`, `Ordering.Domain/Inventory/ProductStock.cs`, `Ordering.Application/Inventory/RequestRestockDomainEventHandler.cs`, `Ordering.Domain/Inventory/RestockRequest.cs` | `DomainEventTests.An_event_raised_by_a_handler_is_published_in_a_second_round_before_the_same_save` |
| A failing handler saves nothing: 422 | `Ordering.Application/Inventory/AllocateStockDomainEventHandler.cs`, `Shop.Common/Errors/BusinessRuleException.cs` | `DomainEventTests.A_failing_handler_answers_422_and_saves_neither_the_order_nor_the_changes_of_the_handlers_before_it` |
| A save conflicting with another request answers 409 and saves nothing: row versions (PostgreSQL's `xmin`) on the order, the stock and the shipment, and the unique number of a history entry | `Property<uint>("RowVersion").IsRowVersion()` in `Ordering.Infrastructure/OrderingDbContext.cs` and `Shipping/Infrastructure/ShippingDbContext.cs`, `Shop.Api/Errors/ApiExceptionFilter.cs` | `ConcurrencyTests.Two_orders_of_the_last_items_at_once_sell_no_more_than_the_stock_and_the_later_answers_409`, `ConcurrencyTests.A_cancellation_crossing_the_delivery_of_the_dispatch_answers_409_and_the_order_stays_shipped` |

### Integration events and the outbox

The modules work together only through integration events, contracts in `Shop.Common`, each module writing to the
outbox of its own `DbContext`: ordering writes `OrderPlacedIntegrationEvent` and `OrderCancelledIntegrationEvent`,
shipping `ShipmentDispatchedIntegrationEvent`. The outbox processor delivers them after the change is saved, from both
outboxes (stores `Ordering` and `Shipping`). The tests turn the processor off (`UsePhemeOutboxForTests` in
`ShopFixture.cs`) and deliver with `DrainOutbox` of `Pheme.Testing`; the customer notifier and the carrier the handlers
call are fakes there (`FakeCustomerNotifier.cs`, `FakeCarrier.cs`). The tests that count attempts, of maintenance, of
the outbox health check and of concurrency run an instance of the application on a database of its own
(`CreateFactoryWithOwnDatabase` in `ShopFixture.cs`), so its outbox holds only their messages; the tests of concurrency
send the other request at a set point, right before a save (`BeforeNextSaveInterceptor.cs`).

| Use case | Code | Tests |
|---|---|---|
| A domain event handler writes an integration event through `IOutboxWriter<OrderingModule>`, saved with the order; once delivered, shipping prepares the shipment and ordering confirms the order to the customer | `Ordering.Application/Orders/PlaceOrder/WriteOrderPlacedToOutboxDomainEventHandler.cs`, `Ordering.Application/OrderingModule.cs`, `Shop.Common/Contracts/OrderPlacedIntegrationEvent.cs`, `Shipping/Application/PrepareShipment/`, `Ordering.Application/Orders/Confirmation/`, `UseOutbox` in `Ordering.Infrastructure/OrderingServiceCollectionExtensions.cs`, `AddPhemeOutbox` in `Ordering.Infrastructure/OrderingDbContext.cs`, `AddOutbox` in `Shop.Api/Program.cs` | `OutboxTests.Placing_an_order_writes_an_integration_event_that_prepares_the_shipment_once_delivered` |
| A cancelled order stops its shipment through the outbox (`POST /orders/{id}/cancel`): shipping cancels the shipment and leaves a notice for the warehouse; delivered again, the cancellation changes nothing | `Ordering.Application/Orders/CancelOrder/`, `Shop.Common/Contracts/OrderCancelledIntegrationEvent.cs`, `Shipping/Application/CancelShipment/`, `Shipping/Domain/Shipment.cs` | `OutboxTests.Cancelling_an_order_cancels_its_shipment_once_the_integration_event_is_delivered`, `OutboxTests.A_redelivered_cancellation_changes_nothing_more` |
| An order cancelled before its placement is delivered is not confirmed: the confirmation handler reads the order of its own module | `Ordering.Application/Orders/Confirmation/SendOrderConfirmationIntegrationEventHandler.cs` | `OutboxTests.An_order_cancelled_before_its_placement_is_delivered_is_not_confirmed_to_the_customer` |
| A cancellation delivered while the delivery of the placement still fails: shipping saves the shipment cancelled, so the retried placement prepares nothing (shipping does not read the orders of the other module) | `Shipping/Application/CancelShipment/`, `Shipping/Domain/Shipment.cs`, `Shipping/Application/PrepareShipment/PrepareShipmentIntegrationEventHandler.cs` | `OutboxTests.A_cancellation_delivered_while_the_placement_still_fails_gets_no_shipment_prepared` |
| A second outbox, in the module with layers as folders: a dispatched shipment (`POST /shipments/{orderId}/dispatch`) marks its order shipped, whose domain event records the history; delivered again, the dispatch changes nothing | `Shipping/Application/DispatchShipment/`, `Shipping/Application/ShippingModule.cs`, `UseOutbox` in `Shipping/Infrastructure/ShippingServiceCollectionExtensions.cs`, `AddPhemeOutbox` in `Shipping/Infrastructure/ShippingDbContext.cs`, `Shop.Common/Contracts/ShipmentDispatchedIntegrationEvent.cs`, `Ordering.Application/Orders/MarkShipped/`, `Shop.Api/Controllers/ShipmentsController.cs` | `ShipmentDispatchTests.Dispatching_a_shipment_marks_its_order_shipped_once_the_integration_event_is_delivered`, `ShipmentDispatchTests.A_redelivered_dispatch_ships_the_order_once`, `ShipmentDispatchTests.Dispatching_a_cancelled_shipment_answers_422`, `ShipmentDispatchTests.Dispatching_an_order_without_a_shipment_answers_404` |
| A module decides from its own data: ordering refuses to cancel a shipped order (422) | `Ordering.Application/Orders/CancelOrder/CancelOrderCommandHandler.cs`, `Ordering.Domain/Orders/Order.cs` | `ShipmentDispatchTests.A_shipped_order_cannot_be_cancelled` |
| Eventual consistency: an order cancelled while its shipment is dispatched stays cancelled, and shipping, reached by the cancellation after the dispatch, asks for the parcel back | `Shipping/Domain/Shipment.cs` (`Cancel`), `Shipping/Application/NotifyWarehouse/NotifyWarehouseDomainEventHandler.cs`, `Ordering.Domain/Orders/Order.cs` (`MarkShipped`) | `ShipmentDispatchTests.An_order_cancelled_while_its_shipment_is_dispatched_gets_its_parcel_back` |
| A delivery losing a race with a request fails on the row version, and its retry sees the change | `Shipping/Application/CancelShipment/CancelShipmentIntegrationEventHandler.cs`, `Shipping/Infrastructure/ShippingDbContext.cs` | `ConcurrencyTests.A_cancellation_delivered_while_the_shipment_is_dispatched_is_retried_and_asks_for_the_parcel_back` |
| A property added to an integration event: a message written before reads its default ([Operating the outbox](../docs/guide/09-operating-the-outbox.md#changing-an-integration-event)) | `Shop.Common/Contracts/OrderPlacedIntegrationEvent.cs` (`Express`), `Shipping/Application/PrepareShipment/PrepareShipmentIntegrationEventHandler.cs` | `OutboxTests.A_message_written_before_a_property_was_added_is_delivered_with_its_default` |
| A handler failing after the event was added: 422 and nothing in the outbox | `Ordering.Application/OrderingApplicationServiceCollectionExtensions.cs`, `Ordering.Application/Inventory/AllocateStockDomainEventHandler.cs` | `OutboxTests.An_order_rejected_after_its_integration_event_was_added_leaves_nothing_in_the_outbox`, `OutboxTests.The_integration_event_is_added_before_the_stock_allocation_that_rejects_an_order` |
| Two handlers of one event in different modules, one failing: the next attempt calls only the failed one | `Shipping/Application/PrepareShipment/PrepareShipmentIntegrationEventHandler.cs`, `Ordering.Application/Orders/Confirmation/SendOrderConfirmationIntegrationEventHandler.cs` | `OutboxTests.A_failed_handler_is_retried_alone_while_the_handler_of_the_other_module_ran_once` |
| The outbox processor in the background, its polling interval from the configuration | `AddOutbox` in `Shop.Api/Program.cs` | `OutboxTests.The_outbox_processor_delivers_in_the_background_without_the_test_helpers` |
| A failing handler retried until it succeeds (each drain one attempt: the tests set `RetryDelay` to zero) | `Shipping/Application/PrepareShipment/PrepareShipmentIntegrationEventHandler.cs` | `OutboxMaintenanceTests.A_handler_failing_twice_is_retried_until_it_succeeds` |
| A scope per handler: a failed attempt saves its outcome, not the shipment its handler added, and the next message's handler does not save it either | `Shipping/Application/PrepareShipment/PrepareShipmentIntegrationEventHandler.cs` | `OutboxMaintenanceTests.A_failed_delivery_saves_its_outcome_but_not_the_shipment_its_handler_added` |
| A message failing every attempt parked with its error, the next messages still delivered | `Shop.Api/Controllers/OutboxController.cs` (`GET /admin/outbox/parked`) | `OutboxMaintenanceTests.A_message_failing_every_attempt_is_parked_with_its_error_and_the_next_messages_are_still_delivered` |
| A parked message requeued through an administration endpoint once the cause is fixed: only the failed handler runs again | `Shop.Api/Controllers/OutboxController.cs` (`POST /admin/outbox/{id}/requeue`), `IOutboxMaintenance` | `OutboxMaintenanceTests.A_parked_message_requeued_through_the_admin_endpoint_once_fixed_is_delivered_to_the_failed_handler_only` |
| The outbox health check for monitoring, `GET /health/outbox`: unhealthy while a message is parked, degraded while one waits longer than 5 minutes; `GET /health`, for the probes of the host, leaves the outbox out, so a parked message takes no instance out of traffic | `AddPhemeOutboxCheck` and `MapHealthChecks` in `Shop.Api/Program.cs`, `Shop.Api/Health/HealthCheckTags.cs` | `OutboxMaintenanceTests.A_parked_message_requeued_through_the_admin_endpoint_once_fixed_is_delivered_to_the_failed_handler_only`, `OutboxMaintenanceTests.The_outbox_check_is_degraded_while_a_message_waits_longer_than_five_minutes` |
| A module left out of the composition root: the integration events of the contracts only it handled fail the start, not the commands that write them | `AddHandlersFromAssemblyContaining<OrderPlacedIntegrationEvent>` in `Shop.Api/Program.cs` | `ModuleRegistrationTests` (in `tests/Pheme.Examples.UnitTests`) |

### Layer boundaries

Architecture tests keeping each layer to what it may use of Pheme, with `PhemeLayerRule` of `Pheme.Testing` (see
[Layers](../docs/guide/10-layers.md) in the documentation):

| Use case | Tests |
|---|---|
| Layers as assemblies (ordering, and the contracts between the modules in `Shop.Common`): the Pheme assemblies each assembly references and the store contracts in its type signatures, against the rule of its layer | `AssemblyLayerTests` |
| Layers as folders of one assembly (shipping): the signatures of the types in each layer's namespace, against the rule of its layer | `NamespaceLayerTests` |

Each has a test showing that the rule fails on code breaking it; the rules themselves are tested in
`tests/Pheme.Testing.UnitTests`.

### Host

| Use case | Code | Tests |
|---|---|---|
| OpenTelemetry: Pheme's spans inside the request's trace, the domain event and database spans inside the command's, the outbox delivery in a trace of its own linked to it, and Pheme's metrics; exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set | `AddOpenTelemetry` in `Shop.Api/Program.cs` | `TelemetryTests` |
| `GET /health` checks both modules' databases; `GET /health/outbox` only the outbox (see above) | `AddHealthChecks` in `Shop.Api/Program.cs` | `HealthTests` |
| One schema per module in one database | `Shop.Common/Persistence/DatabaseSchemaExtensions.cs` | `SchemaTests` |
