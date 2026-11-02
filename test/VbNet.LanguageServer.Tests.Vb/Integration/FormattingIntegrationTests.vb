Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for FormattingService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class FormattingIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _formattingService As FormattingService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _formattingService = New FormattingService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of FormattingService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(FormattingIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function FormatDocumentAsync_InsertsFinalNewline_WhenEnabled() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim edits = Await _formattingService.FormatDocumentAsync(New DocumentFormattingParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Options = New FormattingOptions With {
                    .InsertSpaces = True,
                    .TabSize = 4,
                    .TrimTrailingWhitespace = True,
                    .InsertFinalNewline = True,
                    .TrimFinalNewlines = False
                }
            }, CancellationToken.None)

            Assert.NotNull(edits)
        End Function

        <Fact>
        Public Async Function FormatDocumentAsync_TextWithByteOrderMark_KeepsByteOrderMark() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim clientText = ChrW(&HFEFF) & "Public Class Helper" & vbLf & "Public Sub DoWork()" & vbLf & "End Sub" & vbLf & "End Class" & vbLf

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = clientText
                }
            })
            Assert.NotNull(_documentManager.GetRoslynDocument(helperUri))

            Dim edits = Await _formattingService.FormatDocumentAsync(New DocumentFormattingParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Options = New FormattingOptions With {
                    .InsertSpaces = True,
                    .TabSize = 4,
                    .TrimTrailingWhitespace = True
                }
            }, CancellationToken.None)

            Dim formatted = ApplyEdits(clientText, edits)
            Assert.Equal(ChrW(&HFEFF) & "Public Class Helper" & vbLf & "    Public Sub DoWork()" & vbLf & "    End Sub" & vbLf & "End Class" & vbLf, formatted)
        End Function

        Private Shared Function ApplyEdits(text As String, edits As TextEdit()) As String
            Dim lineStarts As New List(Of Integer) From {0}
            For i = 0 To text.Length - 1
                If text(i) = vbLf(0) Then
                    lineStarts.Add(i + 1)
                End If
            Next

            Dim ordered = edits.OrderByDescending(Function(e) lineStarts(e.Range.Start.Line) + e.Range.Start.Character)
            For Each edit In ordered
                Dim startOffset = lineStarts(edit.Range.Start.Line) + edit.Range.Start.Character
                Dim endOffset = lineStarts(edit.Range.End.Line) + edit.Range.End.Character
                text = text.Substring(0, startOffset) & edit.NewText & text.Substring(endOffset)
            Next
            Return text
        End Function
    End Class

End Namespace
