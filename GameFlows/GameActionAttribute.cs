namespace MG.Server.GameFlows
{
    /// <summary>
    /// Marks a game-flow method as a client-invokable action.
    /// <para>
    /// SECURITY: <see cref="BaseGameFlow.ExecuteAction"/> dispatches by the client-supplied
    /// action name via reflection. Only methods decorated with this attribute may be invoked,
    /// which prevents a client from naming and calling arbitrary methods on the flow object
    /// (the original code called <c>GetMethod(actionId).Invoke(...)</c> with no allow-list).
    /// </para>
    /// A valid game action must be a public instance method taking a single
    /// <c>ExecuteActionData</c> parameter and returning <c>Task</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class GameActionAttribute : Attribute
    {
    }
}
