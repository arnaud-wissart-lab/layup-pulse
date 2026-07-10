namespace LayupPulse.Domain;

/// <summary>
/// Évalue les transitions de la cellule sans état interne, horloge ni dépendance technologique.
/// </summary>
public static class MachineStateMachine
{
    public static StateTransitionResult Transition(MachineSnapshot snapshot, MachineCommand command)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command);

        if (command.Type == MachineCommandType.Disconnected)
        {
            return Accept(snapshot, Disconnect(snapshot, command.Timestamp));
        }

        return command.Type switch
        {
            MachineCommandType.ConnectRequested => Connect(snapshot, command),
            MachineCommandType.ConnectionEstablished => EstablishConnection(snapshot, command),
            MachineCommandType.ConnectionFailed => FailConnection(snapshot, command),
            MachineCommandType.LoadRecipe => LoadRecipe(snapshot, command),
            MachineCommandType.Start => Start(snapshot, command),
            MachineCommandType.Pause => Pause(snapshot, command),
            MachineCommandType.Resume => Resume(snapshot, command),
            MachineCommandType.Stop => Stop(snapshot, command),
            MachineCommandType.CycleCompleted => CompleteCycle(snapshot, command),
            MachineCommandType.Reset => Reset(snapshot, command),
            MachineCommandType.CriticalFaultRaised => RaiseCriticalFault(snapshot, command),
            MachineCommandType.FaultCleared => ClearFault(snapshot, command),
            _ => Reject(
                snapshot,
                StateTransitionRejectionCode.UnsupportedCommand,
                $"La commande {command.Type} n’est pas prise en charge."),
        };
    }

    private static StateTransitionResult Connect(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Disconnected)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Disconnected);
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Connecting,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult EstablishConnection(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Connecting)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Connecting);
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Ready,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult FailConnection(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Connecting)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Connecting);
        }

        return Accept(snapshot, Disconnect(snapshot, command.Timestamp));
    }

    private static StateTransitionResult LoadRecipe(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Ready)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Ready);
        }

        if (command.Recipe is null)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.RecipeRequired,
                "Une recette est requise pour la commande de chargement.");
        }

        RecipeValidationResult validation = command.Recipe.Validate();
        if (!validation.IsValid)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.InvalidRecipe,
                "La recette ne respecte pas les limites simulées.",
                validation.Errors);
        }

        return Accept(snapshot, snapshot with
        {
            LoadedRecipe = command.Recipe,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult Start(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Ready)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Ready);
        }

        if (snapshot.LoadedRecipe is null)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.RecipeRequired,
                "Une recette valide doit être chargée avant le démarrage.");
        }

        RecipeValidationResult validation = snapshot.LoadedRecipe.Validate();
        if (!validation.IsValid)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.InvalidRecipe,
                "La recette chargée n’est pas valide.",
                validation.Errors);
        }

        Guid runId = command.ProductionRunId ?? command.CorrelationId;
        if (runId == Guid.Empty)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.ProductionRunMissing,
                "Un identifiant d’exécution valide est requis pour démarrer.");
        }

        ProductionRun run = ProductionRun.Start(runId, snapshot.LoadedRecipe, command.Timestamp);
        return Accept(snapshot, snapshot with
        {
            State = MachineState.Running,
            CurrentRun = run,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult Pause(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Running)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Running);
        }

        if (snapshot.CurrentRun is null)
        {
            return RejectMissingRun(snapshot);
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Paused,
            CurrentRun = snapshot.CurrentRun.Pause(),
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult Resume(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Paused)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Paused);
        }

        if (snapshot.CurrentRun is null)
        {
            return RejectMissingRun(snapshot);
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Running,
            CurrentRun = snapshot.CurrentRun.Resume(),
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult Stop(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State is not (MachineState.Running or MachineState.Paused))
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Running, MachineState.Paused);
        }

        if (snapshot.CurrentRun is null)
        {
            return RejectMissingRun(snapshot);
        }

        ProductionRun abortedRun = snapshot.CurrentRun.Abort(
            command.Timestamp,
            MachineState.Ready,
            "Arrêt demandé par l’opérateur.");

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Ready,
            CurrentRun = abortedRun,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult CompleteCycle(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Running)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Running);
        }

        if (snapshot.CurrentRun is null)
        {
            return RejectMissingRun(snapshot);
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Completed,
            CurrentRun = snapshot.CurrentRun.Complete(command.Timestamp),
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult Reset(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State == MachineState.Completed)
        {
            return Accept(snapshot, snapshot with
            {
                State = MachineState.Ready,
                Timestamp = command.Timestamp,
            });
        }

        if (snapshot.State != MachineState.Faulted)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Completed, MachineState.Faulted);
        }

        if (snapshot.ActiveFaults.Count > 0)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.ActiveFaultsRemain,
                "Toutes les conditions de défaut actives doivent être levées avant le reset.");
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Ready,
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult RaiseCriticalFault(MachineSnapshot snapshot, MachineCommand command)
    {
        if (!IsConnected(snapshot.State))
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.InvalidState,
                "Un défaut critique ne peut être appliqué que lorsque la session est connectée.");
        }

        if (command.Fault is null)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.FaultRequired,
                "Le type de défaut critique est requis.");
        }

        ProductionRun? currentRun = snapshot.CurrentRun;
        if (currentRun?.Status is ProductionRunStatus.Running or ProductionRunStatus.Paused)
        {
            currentRun = currentRun.Fail(command.Timestamp, $"Défaut critique : {command.Fault.Value}.");
        }

        return Accept(snapshot, snapshot with
        {
            State = MachineState.Faulted,
            CurrentRun = currentRun,
            ActiveFaults = snapshot.ActiveFaults.Add(command.Fault.Value),
            Timestamp = command.Timestamp,
        });
    }

    private static StateTransitionResult ClearFault(MachineSnapshot snapshot, MachineCommand command)
    {
        if (snapshot.State != MachineState.Faulted)
        {
            return RejectInvalidState(snapshot, command.Type, MachineState.Faulted);
        }

        if (command.Fault is null)
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.FaultRequired,
                "Le type de défaut levé est requis.");
        }

        if (!snapshot.ActiveFaults.Contains(command.Fault.Value))
        {
            return Reject(
                snapshot,
                StateTransitionRejectionCode.FaultNotActive,
                "Le défaut indiqué n’est pas actif.");
        }

        return Accept(snapshot, snapshot with
        {
            ActiveFaults = snapshot.ActiveFaults.Remove(command.Fault.Value),
            Timestamp = command.Timestamp,
        });
    }

    private static MachineSnapshot Disconnect(MachineSnapshot snapshot, DateTimeOffset timestamp)
    {
        ProductionRun? currentRun = snapshot.CurrentRun;
        if (currentRun?.Status is ProductionRunStatus.Running or ProductionRunStatus.Paused)
        {
            currentRun = currentRun.Abort(
                timestamp,
                MachineState.Disconnected,
                "Déconnexion de la session machine.");
        }

        return snapshot with
        {
            State = MachineState.Disconnected,
            Timestamp = timestamp,
            LoadedRecipe = null,
            CurrentRun = currentRun,
            ActiveFaults = [],
        };
    }

    private static bool IsConnected(MachineState state) => state is
        MachineState.Ready or
        MachineState.Running or
        MachineState.Paused or
        MachineState.Faulted or
        MachineState.Completed;

    private static StateTransitionResult Accept(MachineSnapshot previous, MachineSnapshot current) =>
        StateTransitionResult.Accepted(previous.State, current);

    private static StateTransitionResult RejectMissingRun(MachineSnapshot snapshot) =>
        Reject(
            snapshot,
            StateTransitionRejectionCode.ProductionRunMissing,
            "Aucune exécution de production active n’est disponible.");

    private static StateTransitionResult RejectInvalidState(
        MachineSnapshot snapshot,
        MachineCommandType commandType,
        params MachineState[] expectedStates)
    {
        string expected = string.Join(" ou ", expectedStates);
        return Reject(
            snapshot,
            StateTransitionRejectionCode.InvalidState,
            $"La commande {commandType} est invalide depuis l’état {snapshot.State}. État attendu : {expected}.");
    }

    private static StateTransitionResult Reject(
        MachineSnapshot snapshot,
        StateTransitionRejectionCode code,
        string message,
        IEnumerable<RecipeValidationError>? validationErrors = null) =>
        StateTransitionResult.Rejected(
            snapshot,
            new StateTransitionRejection(code, message, validationErrors));
}
