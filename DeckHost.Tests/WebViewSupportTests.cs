using System.Runtime.InteropServices;
using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>When the built-in web view may be used: the runtime, and a failure remembered for that runtime version.</summary>
public sealed class WebViewSupportTests : IDisposable
{
    private const string Runtime = "130.0.1";
    private const string NewerRuntime = "131.0.0";
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int Aborted = unchecked((int)0x80004004);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-webview-" + Guid.NewGuid().ToString("N"));

    public WebViewSupportTests()
    {
        WebViewSupport.MarkerPath = Path.Combine(_folder, "webview-disabled.txt");
        WebViewSupport.RuntimeVersion = () => Runtime;
        WebViewSupport.Reset();
    }

    public void Dispose()
    {
        WebViewSupport.RestoreDefaults();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void RememberAFailureOn(string runtime)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(WebViewSupport.MarkerPath, string.Join(Environment.NewLine, runtime, "0x80040154", "COMException: Class not registered") + Environment.NewLine);
    }

    private void RememberAnOlderVersionsFailureOn(string runtime)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(WebViewSupport.MarkerPath, runtime + Environment.NewLine + "COMException: Operation aborted" + Environment.NewLine);
    }

    private static Exception ExceptionFromTheWebViewAdapter()
    {
        try
        {
            InitializeWebViewAdapter();
            return new InvalidOperationException();
        }
        catch (COMException exception)
        {
            return exception;
        }
    }

    private static void InitializeWebViewAdapter() => throw new COMException("Class not registered", ClassNotRegistered);

    [Test]
    public void IsAvailable_RuntimeFound_IsTrue()
    {
        // Arrange (a runtime and no remembered failure)

        // Act
        var available = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(available, Is.True);
    }

    [Test]
    public void IsAvailable_NoRuntime_IsFalse()
    {
        // Arrange
        WebViewSupport.RuntimeVersion = () => null;
        WebViewSupport.Reset();

        // Act
        var available = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(available, Is.False);
    }

    [Test]
    public void IsAvailable_FailureRememberedOnTheSameRuntime_IsFalse()
    {
        // Arrange
        RememberAFailureOn(Runtime);
        WebViewSupport.Reset();

        // Act
        var available = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(available, Is.False);
    }

    [Test]
    public void IsAvailable_MarkerFromAnOlderDeckHost_IsIgnoredAndRemoved()
    {
        // Arrange (an older DeckHost kept every failure, also a view torn down while it started)
        RememberAnOlderVersionsFailureOn(Runtime);

        // Act
        var available = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(available, Is.True);
        Assert.That(File.Exists(WebViewSupport.MarkerPath), Is.False);
    }

    [Test]
    public void Reason_FailureRememberedOnTheSameRuntime_SaysWhatFailed()
    {
        // Arrange
        RememberAFailureOn(Runtime);

        // Act
        _ = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(WebViewSupport.Reason, Does.Contain("Class not registered"));
    }

    [Test]
    public void Reason_NoRuntime_SaysItIsNotInstalledOrNotFound()
    {
        // Arrange
        WebViewSupport.RuntimeVersion = () => null;
        WebViewSupport.Reset();

        // Act
        _ = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(WebViewSupport.Reason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Retry_AfterARememberedFailure_TriesTheWebViewAgain()
    {
        // Arrange
        RememberAFailureOn(Runtime);
        _ = WebViewSupport.IsAvailable;

        // Act
        WebViewSupport.Retry();

        // Assert
        Assert.That(WebViewSupport.IsAvailable, Is.True);
        Assert.That(File.Exists(WebViewSupport.MarkerPath), Is.False);
    }

    [Test]
    public void IsAvailable_FailureRememberedOnAnOlderRuntime_TriesAgain()
    {
        // Arrange
        RememberAFailureOn(Runtime);
        WebViewSupport.RuntimeVersion = () => NewerRuntime;
        WebViewSupport.Reset();

        // Act
        var available = WebViewSupport.IsAvailable;

        // Assert
        Assert.That(available, Is.True);
    }

    [Test]
    public void ReportFailure_WebViewThatFailedWhileRunning_MakesItUnavailable()
    {
        // Arrange
        var exception = ExceptionFromTheWebViewAdapter();

        // Act
        WebViewSupport.ReportFailure(exception);

        // Assert
        Assert.That(WebViewSupport.IsAvailable, Is.False);
    }

    [Test]
    public void ReportFailure_WebViewThatFailedWhileRunning_RemembersTheRuntimeItFailedOn()
    {
        // Arrange
        var exception = ExceptionFromTheWebViewAdapter();

        // Act
        WebViewSupport.ReportFailure(exception);

        // Assert
        Assert.That(File.ReadLines(WebViewSupport.MarkerPath).First(), Is.EqualTo(Runtime));
    }

    [Test]
    public void ReportFailure_WebViewTornDownWhileStarting_KeepsWebViewsAvailable()
    {
        // Arrange (E_ABORT: the editor was rebuilt while the control started; the runtime itself is fine)
        var aborted = new COMException("Operation aborted", Aborted);

        // Act
        WebViewSupport.ReportFailure(aborted);

        // Assert
        Assert.That(WebViewSupport.IsAvailable, Is.True, "the next web view is created as usual");
        Assert.That(File.Exists(WebViewSupport.MarkerPath), Is.False, "and the next start tries the web view again");
    }

    [Test]
    public void ReportFailure_WebViewTornDownWhileStarting_RaisesFailedAsNotLasting()
    {
        // Arrange
        bool? lasting = null;
        void OnFailed(bool value) => lasting = value;
        WebViewSupport.Failed += OnFailed;

        // Act
        WebViewSupport.ReportFailure(new COMException("Operation aborted", Aborted));
        WebViewSupport.Failed -= OnFailed;

        // Assert
        Assert.That(lasting, Is.False);
    }

    [TestCase(ClassNotRegistered, true)]
    [TestCase(Aborted, false)]
    public void IsRuntimeUnusable_ByErrorCode_IsTrueOnlyWhenTheRuntimeCantBeUsed(int hresult, bool expected)
    {
        // Act
        var unusable = WebViewSupport.IsRuntimeUnusable(new COMException("failure", hresult));

        // Assert
        Assert.That(unusable, Is.EqualTo(expected));
    }

    [Test]
    public void ReportFailure_WebViewThatFailedWhileRunning_RaisesFailed()
    {
        // Arrange
        var raised = 0;
        void OnFailed(bool lasting) => raised++;
        WebViewSupport.Failed += OnFailed;

        // Act
        WebViewSupport.ReportFailure(ExceptionFromTheWebViewAdapter());
        WebViewSupport.Failed -= OnFailed;

        // Assert
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void IsWebViewFailure_ComExceptionFromTheWebViewAdapter_IsTrue()
    {
        // Arrange
        var exception = ExceptionFromTheWebViewAdapter();

        // Act
        var isFailure = WebViewSupport.IsWebViewFailure(exception);

        // Assert
        Assert.That(isFailure, Is.True);
    }

    [Test]
    public void IsWebViewFailure_UnrelatedException_IsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("nothing to do with a web view");

        // Act
        var isFailure = WebViewSupport.IsWebViewFailure(exception);

        // Assert
        Assert.That(isFailure, Is.False);
    }
}
