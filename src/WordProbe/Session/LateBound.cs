// By-name access to COM properties through IDispatch. The embedded interop types carry only the members
// the code names statically, so anything addressed by a string (options from a list, the Undo control)
// goes through Type.InvokeMember on the runtime COM object instead.
static class LateBound
{
    public static object? Get(object comObject, string property) =>
        comObject.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, comObject, null, CultureInfo.InvariantCulture);

    public static object? Get(object comObject, string property, params object[] arguments) =>
        comObject.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, comObject, arguments, CultureInfo.InvariantCulture);

    public static void Set(object comObject, string property, object value) =>
        comObject.GetType().InvokeMember(property, System.Reflection.BindingFlags.SetProperty, null, comObject, [value], CultureInfo.InvariantCulture);

    public static object? Call(object comObject, string method, params object?[] arguments) =>
        comObject.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, comObject, arguments, CultureInfo.InvariantCulture);

    /// <summary>Unwraps TargetInvocationException to the COMException Word threw.</summary>
    public static T Try<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (System.Reflection.TargetInvocationException)
        {
            return fallback;
        }
        catch (COMException)
        {
            return fallback;
        }
        catch (MissingMemberException)
        {
            return fallback;
        }
    }
}
