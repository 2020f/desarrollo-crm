using System.Text;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Files;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class FileIntegrationTests(SqlServerTestDatabase database)
    : IClassFixture<SqlServerTestDatabase>
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS9sAAAAASUVORK5CYII=");

    [Fact]
    public async Task Private_upload_without_a_context_is_listed_for_its_owner_and_hidden_even_from_org_admin()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var owner = await AddMemberAsync(db, admin, Permission.Leer);
        var peer = await AddMemberAsync(db, admin, Permission.Leer);
        var files = Service(db, owner, disk);
        var uploaded = await UploadPngAsync(files);
        Assert.Equal(uploaded.Id, uploaded.StoredFileId);
        Assert.Equal($"ARC-{uploaded.Id:N}", uploaded.Code);
        Assert.Equal("Private", uploaded.Visibility);
        Assert.True(uploaded.CanDelete);
        Assert.Null(uploaded.ProjectId);
        Assert.Equal("image/png", uploaded.ContentType);
        Assert.Equal(Png.Length, uploaded.SizeBytes);
        var listing = await files.ListAsync(new(Visibility: FileVisibility.Private));
        Assert.Equal(1, listing.Total);
        Assert.Equal(uploaded.Id, Assert.Single(listing.Items).Id);
        Assert.Equal(Png, await ContentsAsync(await files.OpenAsync(uploaded.Id)));
        Assert.Equal(Png, await ContentsAsync(await files.OpenAsync(uploaded.Id, preview: true)));
        foreach (var outsider in new[] { peer, admin })
        {
            var otherFiles = Service(db, outsider, disk);
            Assert.Empty((await otherFiles.ListAsync(new(Visibility: FileVisibility.Private))).Items);
            await AssertDeniedAsync(() => otherFiles.OpenAsync(uploaded.Id));
            await AssertDeniedAsync(() => otherFiles.OpenAsync(uploaded.Id, preview: true));
            await AssertDeniedAsync(() => otherFiles.DeleteAsync(uploaded.Id));
        }
        var metadata = await db.StoredFiles.AsNoTracking().SingleAsync(x => x.Id == uploaded.Id);
        Assert.DoesNotContain("avatar", metadata.InternalName, StringComparison.OrdinalIgnoreCase);
        Assert.False(Path.IsPathRooted(metadata.InternalName));
        Assert.False(await db.Attachments.AnyAsync(x => x.StoredFileId == uploaded.Id));
    }

    [Fact]
    public async Task Public_project_files_use_current_read_permission_before_pagination_and_download()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var allowed = await AddMemberAsync(db, admin, Permission.Leer);
        var creatorWithoutRead = await AddMemberAsync(db, admin, Permission.Crear, Permission.Comentar);
        var hiddenProject = await SeedProjectAsync(db, admin);
        var outsider = await AddMemberAsync(db, hiddenProject, Permission.Leer);
        var foreign = await SeedTeamAsync(db);
        await AssertDeniedAsync(() => UploadPngAsync(Service(db, creatorWithoutRead, disk),
            visibility: FileVisibility.Public, projectId: admin.ProjectId));
        await AssertDeniedAsync(() => Service(db, creatorWithoutRead, disk).UploadAsync(
            new("unreadable-context.png", Png.Length, FileVisibility.Context, EntityType: "Project", EntityId: admin.ProjectId), new MemoryStream(Png)));
        var first = await UploadPngAsync(Service(db, admin, disk), visibility: FileVisibility.Public, projectId: admin.ProjectId);
        var second = await UploadPngAsync(Service(db, admin, disk), "second.png", FileVisibility.Public, admin.ProjectId);
        var readable = Service(db, allowed, disk);
        var page1 = await readable.ListAsync(new(ProjectId: admin.ProjectId, Visibility: FileVisibility.Public, PageSize: 1));
        var page2 = await readable.ListAsync(new(ProjectId: admin.ProjectId, Visibility: FileVisibility.Public, Page: 2, PageSize: 1));
        Assert.Equal(2, page1.Total); Assert.Equal(2, page2.Total);
        Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        Assert.All(page1.Items, x => Assert.False(x.CanDelete));
        Assert.Equal(first.Id, Assert.Single((await readable.ListAsync(new(Visibility: FileVisibility.Public, Search: first.Code))).Items).Id);
        Assert.Equal(Png, await ContentsAsync(await readable.OpenAsync(first.Id, preview: true)));
        foreach (var other in new[] { outsider, foreign })
        {
            var denied = Service(db, other, disk);
            var result = await denied.ListAsync(new(Visibility: FileVisibility.Public));
            Assert.Equal(0, result.Total); Assert.Empty(result.Items);
            await AssertDeniedAsync(() => denied.OpenAsync(first.Id));
            await AssertDeniedAsync(() => denied.OpenAsync(second.Id, preview: true));
            await AssertDeniedAsync(() => denied.ListAsync(new(ProjectId: admin.ProjectId, Visibility: FileVisibility.Public)));
        }
    }

    [Theory]
    [InlineData("project")]
    [InlineData("membership")]
    [InlineData("user")]
    public async Task Public_access_is_revoked_for_existing_service_instances(string revoked)
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, admin, Permission.Leer);
        var uploaded = await UploadPngAsync(Service(db, admin, disk), visibility: FileVisibility.Public, projectId: admin.ProjectId);
        var files = Service(db, reader, disk);
        Assert.Equal(1, (await files.ListAsync(new(Visibility: FileVisibility.Public))).Total);
        if (revoked == "project")
            (await db.ProjectMembers.SingleAsync(x => x.ProjectId == admin.ProjectId && x.UserId == reader.UserId)).IsActive = false;
        else if (revoked == "membership")
            (await db.OrganizationMembers.SingleAsync(x => x.OrganizationId == admin.OrgId && x.UserId == reader.UserId)).IsActive = false;
        else (await db.Users.SingleAsync(x => x.Id == reader.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        if (revoked == "project") Assert.Equal(0, (await files.ListAsync(new(Visibility: FileVisibility.Public))).Total);
        else await AssertDeniedAsync(() => files.ListAsync(new(Visibility: FileVisibility.Public)));
        await AssertDeniedAsync(() => files.OpenAsync(uploaded.Id));
        await AssertDeniedAsync(() => files.OpenAsync(uploaded.Id, preview: true));
    }

    [Fact]
    public async Task Context_task_attachment_requires_comment_permission_and_revalidates_access_for_the_uploader()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var commenter = await AddMemberAsync(db, admin, Permission.Leer, Permission.Comentar);
        var reader = await AddMemberAsync(db, admin, Permission.Leer);
        var task = await AddTaskAsync(db, admin);
        var request = new FileUploadRequest("context.png", Png.Length, FileVisibility.Context,
            EntityType: "WorkItem", EntityId: task.Id);
        await AssertDeniedAsync(() => Service(db, reader, disk).UploadAsync(request, new MemoryStream(Png)));
        var uploader = Service(db, commenter, disk);
        var file = await uploader.UploadAsync(request, new MemoryStream(Png));
        Assert.Equal("Context", file.Visibility);
        Assert.Equal(admin.ProjectId, file.ProjectId);
        Assert.Equal(file.Id, Assert.Single((await Service(db, reader, disk).ListAsync(new(Visibility: FileVisibility.Context))).Items).Id);
        Assert.Equal(Png, await ContentsAsync(await Service(db, reader, disk).OpenAsync(file.Id)));
        (await db.ProjectMembers.SingleAsync(x => x.ProjectId == admin.ProjectId && x.UserId == commenter.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(0, (await uploader.ListAsync(new(Visibility: FileVisibility.Context))).Total);
        await AssertDeniedAsync(() => uploader.OpenAsync(file.Id));
    }

    [Fact]
    public async Task Context_document_attachment_obeys_private_document_acl_even_for_an_organization_admin()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, admin, Permission.Leer, Permission.Crear, Permission.Comentar);
        var document = new Document { OrganizationId = admin.OrgId, ProjectId = admin.ProjectId,
            AuthorId = author.UserId, CreatedBy = author.UserId, Title = "Private document",
            Content = "Private legacy text", Visibility = DocumentVisibility.Privada };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var file = await Service(db, author, disk).UploadAsync(new("document.png", Png.Length, FileVisibility.Context,
            EntityType: "Document", EntityId: document.Id), new MemoryStream(Png));
        Assert.Equal(Png, await ContentsAsync(await Service(db, author, disk).OpenAsync(file.Id, preview: true)));
        var adminFiles = Service(db, admin, disk);
        Assert.Equal(0, (await adminFiles.ListAsync(new(Visibility: FileVisibility.Context))).Total);
        await AssertDeniedAsync(() => adminFiles.OpenAsync(file.Id));
        await AssertDeniedAsync(() => adminFiles.OpenAsync(file.Id, preview: true));
    }

    [Fact]
    public async Task Legacy_multi_context_file_cannot_disclose_an_unreadable_target_through_a_list_filter()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, admin, Permission.Leer);
        var hidden = await SeedProjectAsync(db, admin);
        var readableTask = await AddTaskAsync(db, admin);
        var hiddenTask = await AddTaskAsync(db, hidden);
        var file = await AddLegacyBlobAsync(db, disk, admin);
        foreach (var task in new[] { readableTask, hiddenTask }) db.Attachments.Add(new()
        {
            OrganizationId = admin.OrgId, StoredFileId = file.Id, EntityType = "WorkItem", EntityId = task.Id,
            LinkedBy = admin.UserId, CreatedBy = admin.UserId,
        });
        await db.SaveChangesAsync();
        var files = Service(db, reader, disk);
        Assert.Equal(file.Id, Assert.Single((await files.ListAsync(new(Visibility: FileVisibility.Context))).Items).Id);
        Assert.Equal(Png, await ContentsAsync(await files.OpenAsync(file.Id)));
        try
        {
            var filtered = await files.ListAsync(new(EntityType: "WorkItem", EntityId: hiddenTask.Id));
            Assert.Equal(0, filtered.Total); Assert.Empty(filtered.Items);
        }
        catch (AppException error) { Assert.Contains(error.StatusCode, new[] { 403, 404 }); }
    }

    [Fact]
    public async Task Unattached_legacy_context_is_recoverable_only_by_its_uploader()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var owner = await AddMemberAsync(db, admin, Permission.Leer);
        var file = await AddLegacyBlobAsync(db, disk, owner);
        Assert.Equal(file.Id, Assert.Single((await Service(db, owner, disk).ListAsync(new())).Items).Id);
        Assert.Empty((await Service(db, admin, disk).ListAsync(new())).Items);
        await AssertDeniedAsync(() => Service(db, admin, disk).OpenAsync(file.Id));
    }

    [Fact]
    public async Task Delete_requires_the_uploader_and_retains_the_blob_for_recovery()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var admin = await SeedTeamAsync(db);
        var owner = await AddMemberAsync(db, admin, Permission.Leer, Permission.Crear);
        var file = await UploadPngAsync(Service(db, owner, disk), visibility: FileVisibility.Public, projectId: admin.ProjectId);
        var storageKey = (await db.StoredFiles.AsNoTracking().SingleAsync(x => x.Id == file.Id)).InternalName;
        await AssertDeniedAsync(() => Service(db, admin, disk).DeleteAsync(file.Id));
        await using (var deletion = database.NewDb()) await Service(deletion, owner, disk).DeleteAsync(file.Id);
        await using var verify = database.NewDb();
        Assert.True((await verify.StoredFiles.IgnoreQueryFilters().SingleAsync(x => x.Id == file.Id)).IsDeleted);
        Assert.Equal(0, (await Service(verify, owner, disk).ListAsync(new())).Total);
        await AssertDeniedAsync(() => Service(verify, owner, disk).OpenAsync(file.Id));
        await using var retained = await disk.Storage.OpenAsync(storageKey);
        Assert.Equal(Png.Length, retained.Length);
    }

    [Fact]
    public async Task Unsafe_names_extensions_signatures_and_lengths_leave_no_metadata_or_staging_files()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var files = Service(db, team, disk);
        var cases = new (string Name, byte[] Bytes, long Expected)[]
        {
            ("../escape.png", Png, Png.Length), ("C:\\escape.png", Png, Png.Length),
            ("active.svg", Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), 25),
            ("active.html", Encoding.UTF8.GetBytes("<html>not an attachment type</html>"), 34),
            ("fake.png", new byte[32], 32),
            ("binary.md", new byte[] { 0, 1, 2, 3 }, 4),
            ("empty.png", Array.Empty<byte>(), 0),
            ("oversized.png", Png, LocalPrivateFileStorage.MaxBytes + 1),
            ("truncated.png", Png, Png.Length + 1), ("extra.png", Png, Png.Length - 1),
        };
        foreach (var invalid in cases)
            await Assert.ThrowsAsync<ValidationException>(() => files.UploadAsync(new(invalid.Name, invalid.Expected), new MemoryStream(invalid.Bytes)));
        Assert.False(await db.StoredFiles.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.False(await db.Attachments.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.Equal(0, disk.FileCount);
    }

    [Fact]
    public async Task Text_can_be_downloaded_but_not_previewed_as_an_inline_document()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var files = Service(db, team, disk);
        var bytes = Encoding.UTF8.GetBytes("# Private note\nTexto legible.");
        var file = await files.UploadAsync(new("notes.md", bytes.Length), new MemoryStream(bytes));
        Assert.Equal("text/markdown", file.ContentType);
        Assert.Equal(bytes, await ContentsAsync(await files.OpenAsync(file.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => files.OpenAsync(file.Id, preview: true));
    }

    [Fact]
    public async Task Interrupted_copy_removes_partial_bytes_and_does_not_publish_a_file_record()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        await Assert.ThrowsAsync<IOException>(() => Service(db, team, disk).UploadAsync(
            new("interrupted.png", Png.Length), new InterruptedReadStream(Png)));
        Assert.False(await db.StoredFiles.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.Equal(0, disk.FileCount);
    }

    [Fact]
    public async Task Database_failure_rolls_back_metadata_and_removes_the_new_blob()
    {
        using var disk = new FileSandbox();
        await using var setup = database.NewDb();
        var team = await SeedTeamAsync(setup);
        var triggerName = $"RejectStoredFile_{Guid.NewGuid():N}";
        // Only generated identifiers and fixture-owned GUIDs enter this temporary SQL trigger.
#pragma warning disable EF1002
        await setup.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [{triggerName}] ON [StoredFiles] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE OrganizationId = '{team.OrgId:D}') THROW 51000, 'Intentional file persistence failure', 1; END");
        try
        {
            await using var mutation = database.NewDb();
            await Assert.ThrowsAsync<DbUpdateException>(() => UploadPngAsync(Service(mutation, team, disk)));
        }
        finally { await setup.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS [{triggerName}]"); }
#pragma warning restore EF1002
        await using var verify = database.NewDb();
        Assert.False(await verify.StoredFiles.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.False(await verify.Attachments.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.OrganizationId == team.OrgId && x.Action == "FileUploaded"));
        Assert.Equal(0, disk.FileCount);
    }

    [Fact]
    public async Task Tampered_storage_keys_cannot_read_an_existing_file_outside_the_configured_root()
    {
        using var disk = new FileSandbox();
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        Directory.CreateDirectory(disk.BasePath);
        await File.WriteAllBytesAsync(Path.Combine(disk.BasePath, "sentinel.png"), Png);
        var file = new StoredFile { OrganizationId = team.OrgId, UploaderId = team.UserId,
            OriginalName = "safe.png", ContentType = "image/png", SizeBytes = Png.Length,
            InternalName = "../sentinel.png", Visibility = FileVisibility.Private };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, team, disk).OpenAsync(file.Id));
        await Assert.ThrowsAsync<ValidationException>(() => disk.Storage.OpenAsync(Path.Combine(disk.BasePath, "sentinel.png")));
        Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(disk.BasePath, "sentinel.png")));
    }

    private static FileService Service(AppDbContext db, Team team, FileSandbox disk)
    {
        var current = new TestUser(team.UserId, team.OrgId);
        var permissions = new PermissionService(db, current);
        return new(db, current, permissions, disk.Storage, new DocumentService(db, current, permissions));
    }
    private static Task<FileDto> UploadPngAsync(FileService service, string name = "avatar.png",
        FileVisibility visibility = FileVisibility.Private, Guid? projectId = null) =>
        service.UploadAsync(new(name, Png.Length, visibility, projectId), new MemoryStream(Png));
    private static async Task<byte[]> ContentsAsync(FileContent file)
    {
        await using var content = file.Stream;
        using var output = new MemoryStream();
        await content.CopyToAsync(output);
        return output.ToArray();
    }
    private static async Task AssertDeniedAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<AppException>(action);
        Assert.Contains(error.StatusCode, new[] { 403, 404 });
    }
    private static async Task<StoredFile> AddLegacyBlobAsync(AppDbContext db, FileSandbox disk, Team team)
    {
        var file = new StoredFile { OrganizationId = team.OrgId, UploaderId = team.UserId, CreatedBy = team.UserId,
            OriginalName = "legacy.png", Visibility = FileVisibility.Context };
        var blob = await disk.Storage.SaveAsync(team.OrgId, file.Id, ".png", new MemoryStream(Png), Png.Length);
        file.InternalName = blob.Key; file.SizeBytes = blob.SizeBytes; file.ContentType = blob.ContentType;
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file;
    }
    private static async Task<WorkItem> AddTaskAsync(AppDbContext db, Team team)
    {
        var task = new WorkItem { OrganizationId = team.OrgId, ProjectId = team.ProjectId, AuthorId = team.UserId,
            Title = "Attachment context", Code = $"F{Guid.NewGuid():N}"[..14], StateId = team.StateId };
        db.WorkItems.Add(task); await db.SaveChangesAsync(); return task;
    }
    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "File SQL tests", Slug = $"file-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"admin-{suffix}@test.invalid", FullName = "File admin" };
        var role = new Role { OrganizationId = organization.Id, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(organization); db.Users.Add(user); db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = organization.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync();
        return await SeedProjectAsync(db, new(organization.Id, user.Id, Guid.Empty, Guid.Empty));
    }
    private static async Task<Team> SeedProjectAsync(AppDbContext db, Team team)
    {
        var project = new Project { OrganizationId = team.OrgId, Name = "Files project", Code = $"F{Guid.NewGuid():N}"[..12], IsPrivate = true };
        db.Projects.Add(project); await db.SaveChangesAsync();
        await db.EnsureDefaultStatesAsync(project.Id, team.OrgId); await db.SaveChangesAsync();
        return team with { ProjectId = project.Id, StateId = await db.WorkflowStates.Where(x => x.ProjectId == project.Id && x.IsDefault).Select(x => x.Id).SingleAsync() };
    }
    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"member-{suffix}@test.invalid", FullName = "File member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true };
        db.OrganizationMembers.Add(member);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync(); return team with { UserId = user.Id };
    }
    private sealed record Team(Guid OrgId, Guid UserId, Guid ProjectId, Guid StateId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    {
        public string Name => "SQL file test";
        public bool IsAuthenticated => true;
    }
    private sealed class FileSandbox : IDisposable
    {
        public string BasePath { get; } = Path.Combine(Path.GetTempPath(), $"Desarrollo_FileTests_{Guid.NewGuid():N}");
        public string RootPath => Path.Combine(BasePath, "storage");
        public LocalPrivateFileStorage Storage => new(RootPath);
        public int FileCount => Directory.Exists(RootPath) ? Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories).Count() : 0;
        public void Dispose()
        {
            var absolute = Path.GetFullPath(BasePath);
            var name = Path.GetFileName(absolute);
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(absolute), expectedParent)
                || !name.StartsWith("Desarrollo_FileTests_", StringComparison.Ordinal)
                || !Guid.TryParseExact(name["Desarrollo_FileTests_".Length..], "N", out _))
                throw new InvalidOperationException("Refusing to remove a directory not owned by this test.");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
        }
    }
    private sealed class InterruptedReadStream(byte[] bytes) : Stream
    {
        private bool _started;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_started) throw new IOException("Intentional interrupted upload.");
            _started = true;
            var copied = Math.Min(8, count); Array.Copy(bytes, 0, buffer, offset, copied); return copied;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_started) return ValueTask.FromException<int>(new IOException("Intentional interrupted upload."));
            _started = true;
            var copied = Math.Min(8, buffer.Length); bytes.AsSpan(0, copied).CopyTo(buffer.Span); return ValueTask.FromResult(copied);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
