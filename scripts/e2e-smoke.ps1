<#
  Uchidan-uchiga smoke test: ishlab turgan API + MailPit'ga qarshi.
  Ishga tushirish:  docker compose up -d db mailpit ; API'ni RateLimiting__Enabled=false bilan ishga tushiring ; .\scripts\e2e-smoke.ps1
  (Rate limit yoqilgan bo'lsa auth endpoint'lari 10 so'rov/daqiqadan keyin 429 qaytaradi.)
  Parametrlar: -ApiUrl http://localhost:5131 -MailpitUrl http://localhost:8025
#>
param(
    [string]$ApiUrl = 'http://localhost:5131',
    [string]$MailpitUrl = 'http://localhost:8025'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web
Add-Type -AssemblyName System.Drawing

$script:http = New-Object System.Net.Http.HttpClient
$script:http.Timeout = [TimeSpan]::FromSeconds(60)
$script:passed = 0
$script:failed = 0
$run = [Guid]::NewGuid().ToString('N').Substring(0, 6)

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null, [string]$Lang = 'uz',
          [System.Net.Http.HttpContent]$Content = $null)
    $req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method)), ($ApiUrl + $Path)
    $req.Headers.Add('Accept-Language', $Lang)
    if ($Token) { $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue 'Bearer', $Token }
    if ($Content) { $req.Content = $Content }
    elseif ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 20 -Compress
        $req.Content = New-Object System.Net.Http.StringContent $json, ([Text.Encoding]::UTF8), 'application/json'
    }
    $resp = $script:http.SendAsync($req).GetAwaiter().GetResult()
    $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null
    if ($text) { try { $data = $text | ConvertFrom-Json } catch { $data = $text } }
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Data = $data; Raw = $text; Headers = $resp.Headers }
}

function Check([string]$Name, [bool]$Condition, $Details = $null) {
    if ($Condition) { $script:passed++; Write-Host "  [OK]   $Name" -ForegroundColor Green }
    else {
        $script:failed++; Write-Host "  [FAIL] $Name" -ForegroundColor Red
        if ($null -ne $Details) { Write-Host ("         " + (($Details | Out-String).Trim() -replace "`n", "`n         ")) -ForegroundColor DarkYellow }
    }
}

function Get-MailFor([string]$To, [string]$Contains = '') {
    for ($i = 0; $i -lt 30; $i++) {
        $list = Invoke-RestMethod "$MailpitUrl/api/v1/search?query=to:$To" -UseBasicParsing
        foreach ($m in $list.messages) {
            $full = Invoke-RestMethod "$MailpitUrl/api/v1/message/$($m.ID)" -UseBasicParsing
            if (-not $Contains -or $full.Text -like "*$Contains*" -or $full.HTML -like "*$Contains*") { return $full }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-QueryFromMail($Mail, [string]$PathPart) {
    $match = [regex]::Match($Mail.HTML, 'href="([^"]*' + [regex]::Escape($PathPart) + '[^"]*)"')
    if (-not $match.Success) { return $null }
    $uri = [Uri]([System.Web.HttpUtility]::HtmlDecode($match.Groups[1].Value))
    # Vergul: PowerShell kolleksiyani yoyib yubormasligi uchun.
    return , [System.Web.HttpUtility]::ParseQueryString($uri.Query)
}

function New-User([string]$Name) {
    $email = "$Name.$run@e2e.local"
    $password = 'Passw0rd!' + $run
    $reg = Invoke-Api POST '/api/auth/register' @{ email = $email; userName = "$Name$run"; password = $password; confirmPassword = $password; firstName = $Name }
    Check "$Name ro'yxatdan o'tdi" ($reg.Status -eq 200 -and $reg.Data.requiresEmailConfirmation) $reg.Raw

    $early = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $email; password = $password }
    Check "$Name tasdiqlanmagan email bilan kira olmaydi" ($early.Status -ge 400 -and $early.Status -lt 500) $early.Raw

    $mail = Get-MailFor $email 'confirm-email'
    Check "$Name uchun tasdiqlash xati keldi (MailPit)" ($null -ne $mail)
    $q = Get-QueryFromMail $mail 'confirm-email'
    $confirm = Invoke-Api POST '/api/auth/confirm-email' @{ userId = $q['userId']; token = $q['token'] }
    Check "$Name emailini tasdiqladi" ($confirm.Status -eq 204) $confirm.Raw

    $login = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $email; password = $password }
    Check "$Name login qildi" ($login.Status -eq 200 -and $login.Data.accessToken) $login.Raw
    [pscustomobject]@{ Email = $email; UserName = "$Name$run"; Password = $password; Id = $login.Data.user.id
                       Token = $login.Data.accessToken; Refresh = $login.Data.refreshToken }
}

function New-PngContent([int]$Width, [int]$Height) {
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::SteelBlue)
    $g.FillEllipse([System.Drawing.Brushes]::Orange, 10, 10, $Width - 20, $Height - 20)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $file = New-Object System.Net.Http.ByteArrayContent (, $ms.ToArray())
    $file.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue 'image/png'
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $form.Add($file, 'file', 'photo.png')
    $form.Add((New-Object System.Net.Http.StringContent 'Test rasm'), 'altText')
    return , $form
}

Write-Host "`n== Umumiy ==" -ForegroundColor Cyan
$health = Invoke-Api GET '/health'
Check 'GET /health = 200' ($health.Status -eq 200)
$anon = Invoke-Api GET '/api/my/posts'
Check '401 tokensiz, ProblemDetails + code' ($anon.Status -eq 401 -and $anon.Data.code -eq 'General.Unauthorized') $anon.Raw
$badRu = Invoke-Api POST '/api/auth/register' @{ email = 'not-an-email'; userName = 'x'; password = '1'; confirmPassword = '2' } -Lang 'ru'
Check 'Validatsiya xatosi 400, errors bilan' ($badRu.Status -eq 400 -and $badRu.Data.errors) $badRu.Raw
Check 'Xato sarlavhasi rus tilida (Accept-Language: ru)' ($badRu.Data.title -match '[Ѐ-ӿ]') $badRu.Data.title

Write-Host "`n== Auth ==" -ForegroundColor Cyan
$alice = New-User 'alice'
$bob = New-User 'bob'
$me = Invoke-Api GET '/api/auth/me' -Token $alice.Token
Check '/api/auth/me permission''lari bilan' ($me.Status -eq 200 -and ($me.Data.permissions -contains 'Posts.Manage')) $me.Raw

$refresh1 = Invoke-Api POST '/api/auth/refresh' @{ refreshToken = $alice.Refresh }
Check 'Refresh token yangi juftlik beradi' ($refresh1.Status -eq 200 -and $refresh1.Data.refreshToken -ne $alice.Refresh) $refresh1.Raw
$reuse = Invoke-Api POST '/api/auth/refresh' @{ refreshToken = $alice.Refresh }
Check 'Eski refresh token qayta ishlatilsa rad etiladi' ($reuse.Status -eq 401) $reuse.Raw
$afterReuse = Invoke-Api POST '/api/auth/refresh' @{ refreshToken = $refresh1.Data.refreshToken }
Check 'Qayta ishlatishdan keyin butun oila bekor qilinadi' ($afterReuse.Status -eq 401) $afterReuse.Raw
$relogin = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $alice.UserName; password = $alice.Password }
$alice.Token = $relogin.Data.accessToken; $alice.Refresh = $relogin.Data.refreshToken
Check 'Username bilan login' ($relogin.Status -eq 200) $relogin.Raw

Write-Host "`n== Profil ==" -ForegroundColor Cyan
$basic = Invoke-Api PUT '/api/my/profile/basic' @{ firstName = 'Alisa'; lastName = 'Karimova'; displayName = 'Alisa K.'; bio = 'Dasturchi'; location = 'Toshkent'; profession = '.NET developer' } -Token $alice.Token
Check 'Profil asosiy ma''lumotlari yangilandi' ($basic.Status -eq 204) $basic.Raw
$skill1 = Invoke-Api POST '/api/my/profile/skills' @{ name = 'C#'; level = 90 } -Token $alice.Token
$skill2 = Invoke-Api POST '/api/my/profile/skills' @{ name = 'Docker'; level = $null } -Token $alice.Token
Check 'Skill level bilan va level''siz (null) qo''shildi' ($skill1.Status -eq 201 -and $skill2.Status -eq 201) ($skill1.Raw + ' | ' + $skill2.Raw)
$skillBad = Invoke-Api POST '/api/my/profile/skills' @{ name = 'X'; level = 150 } -Token $alice.Token
Check 'Skill level 150 rad etiladi' ($skillBad.Status -eq 400) $skillBad.Raw
$about = Invoke-Api PUT '/api/my/profile/about' @{ aboutMe = '<p>Salom<script>alert(1)</script></p>' } -Token $alice.Token
$profile = Invoke-Api GET '/api/my/profile' -Token $alice.Token
Check 'AboutMe sanitize qilindi (script yo''q)' ($about.Status -eq 204 -and $profile.Raw -notmatch '<script') $profile.Data.aboutMe

Write-Host "`n== Kategoriyalar ==" -ForegroundColor Cyan
$cat = Invoke-Api POST '/api/my/categories' @{ translations = @(
        @{ culture = 'uz'; name = 'Dasturlash' }, @{ culture = 'uz-Cyrl'; name = 'Дастурлаш' },
        @{ culture = 'ru'; name = 'Программирование' }, @{ culture = 'en'; name = 'Programming' }); isActive = $true } -Token $alice.Token
Check 'Kategoriya 4 tilda yaratildi' ($cat.Status -eq 201 -and $cat.Data.slug -eq 'dasturlash') $cat.Raw
$sub = Invoke-Api POST '/api/my/categories' @{ parentId = $cat.Data.id; translations = @(@{ culture = 'uz'; name = '.NET' }); isActive = $true } -Token $alice.Token
Check 'Subkategoriya yaratildi' ($sub.Status -eq 201) $sub.Raw
$treeRu = Invoke-Api GET '/api/my/categories' -Token $alice.Token -Lang 'ru'
Check 'Daraxt ru tilida: nom "Программирование"' ($treeRu.Raw -match 'Программирование' -and ($treeRu.Data | Where-Object { $_.id -eq $cat.Data.id }).name -eq 'Программирование') $treeRu.Raw
$cycle = Invoke-Api PATCH "/api/my/categories/$($cat.Data.id)/move" @{ newParentId = $sub.Data.id; order = 0 } -Token $alice.Token
Check 'Sikl (o''z avlodiga ko''chirish) rad etiladi' ($cycle.Status -ge 400 -and $cycle.Status -lt 500) $cycle.Raw
$bobSeesCat = Invoke-Api GET "/api/my/categories/$($cat.Data.id)" -Token $bob.Token
Check 'Bob Alice kategoriyasini ko''ra olmaydi (404)' ($bobSeesCat.Status -eq 404) $bobSeesCat.Raw

Write-Host "`n== Media ==" -ForegroundColor Cyan
$upload = Invoke-Api POST '/api/my/media' -Token $alice.Token -Content (New-PngContent 1200 800)
Check 'Rasm yuklandi (1200x800)' ($upload.Status -eq 201 -and $upload.Data.width -eq 1200) $upload.Raw
Check 'Variantlar yaratildi (thumb, medium)' ($upload.Data.variants.thumb.width -eq 320 -and $upload.Data.variants.medium.width -eq 960) ($upload.Data.variants | ConvertTo-Json -Compress)
$file = $script:http.GetAsync($ApiUrl + $upload.Data.variants.thumb.url).GetAwaiter().GetResult()
Check '/media orqali fayl beriladi (image/webp, immutable cache)' ($file.StatusCode -eq 'OK' -and $file.Content.Headers.ContentType.MediaType -eq 'image/webp' -and "$($file.Headers.CacheControl)" -match 'immutable') "$($file.StatusCode) $($file.Content.Headers.ContentType) $($file.Headers.CacheControl)"
$fake = New-Object System.Net.Http.MultipartFormDataContent
$fakeFile = New-Object System.Net.Http.ByteArrayContent (, [Text.Encoding]::UTF8.GetBytes('<?php echo 1; ?>'))
$fakeFile.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue 'image/png'
$fake.Add($fakeFile, 'file', 'evil.png')
$fakeUp = Invoke-Api POST '/api/my/media' -Token $alice.Token -Content $fake
Check 'Soxta rasm (magic bytes mos emas) rad etiladi' ($fakeUp.Status -eq 400) $fakeUp.Raw

Write-Host "`n== Postlar ==" -ForegroundColor Cyan
$mediaId = $upload.Data.id
$body = "<h2>Kirish</h2><p>Bu maqolada ajoyibso'z haqida yozaman.</p>" +
        "<figure class=`"image image-style-align-right`" style=`"width:40%;float:right`"><img data-media-id=`"$mediaId`" src=`"https://evil.example/x.png`" onerror=`"alert(1)`"><figcaption>Rasm izohi</figcaption></figure>" +
        "<script>alert('xss')</script><h2>Xulosa</h2><p>Oxiri.</p>"
$create = Invoke-Api POST '/api/my/posts' @{ title = "Birinchi maqola $run"; summary = 'Qisqa'; categoryId = $cat.Data.id; tags = @('dotnet', 'Clean Architecture', 'dotnet');
        allowComments = $true; content = @{ format = 'tiptap-json'; body = $body; raw = @{ type = 'doc'; content = @() } } } -Token $alice.Token
Check 'Post (draft) yaratildi' ($create.Status -eq 201 -and $create.Data.status -eq 'Draft') $create.Raw
$post = $create.Data
$html = $post.content.html
Check 'Sanitize: script va onerror olib tashlandi' ($html -notmatch '<script' -and $html -notmatch 'onerror') $html
Check 'Rasm src kanonik URL''ga almashtirildi, srcset qo''shildi' ($html -notmatch 'evil\.example' -and $html -match 'srcset=' -and $html -match '/media/') $html
Check 'Word''dagi joylashuv saqlandi (float:right, width:40%, figcaption)' ($html -match 'float:\s*right' -and $html -match 'width:\s*40%' -and $html -match '<figcaption>') $html
Check 'Mundarija (toc) 2 ta sarlavha' (@($post.toc).Count -eq 2) ($post.toc | ConvertTo-Json -Compress)
Check 'Teglar dublikatsiz (2 ta)' (@($post.tags).Count -eq 2) ($post.tags | ConvertTo-Json -Compress)
Check 'Raw editor hujjati saqlandi' ($null -ne $post.content.raw) $post.content.raw
Check 'Enum JSON''da matn ("Draft")' ($create.Raw -match '"status":"Draft"') ''

$foreign = Invoke-Api POST '/api/my/posts' @{ title = 'Bob post'; content = @{ format = 'html'; body = "<p><img data-media-id=`"$mediaId`"></p>" } } -Token $bob.Token
Check 'Bob Alice rasmini ishlata olmaydi (Post.InvalidMediaReference)' ($foreign.Status -ge 400 -and $foreign.Data.code -eq 'Post.InvalidMediaReference') $foreign.Raw
$bobSeesPost = Invoke-Api GET "/api/my/posts/$($post.id)" -Token $bob.Token
Check 'Bob Alice postini boshqaruvda ko''ra olmaydi (404)' ($bobSeesPost.Status -eq 404) $bobSeesPost.Raw
$draftPublic = Invoke-Api GET "/api/public/authors/$($alice.UserName)/posts/$($post.slug)"
Check 'Draft public''da ko''rinmaydi (404)' ($draftPublic.Status -eq 404) $draftPublic.Raw

$stale = Invoke-Api PUT "/api/my/posts/$($post.id)" @{ title = 'Eski versiya'; content = @{ format = 'html'; body = '<p>x</p>' }; version = 1 } -Token $alice.Token
Check 'Eskirgan version bilan update 409' ($stale.Status -eq 409) $stale.Raw
$autosave = Invoke-Api PUT "/api/my/posts/$($post.id)/autosave" @{ title = $post.title; content = @{ format = 'html'; body = '<p>Autosave matni</p>' } } -Token $alice.Token
Check 'Autosave saqlandi' ($autosave.Status -eq 200) $autosave.Raw

$publish = Invoke-Api POST "/api/my/posts/$($post.id)/publish" -Token $alice.Token
Check 'Post publish qilindi' ($publish.Status -eq 200 -and $publish.Data.status -eq 'Published') $publish.Raw
$public = Invoke-Api GET "/api/public/authors/$($alice.UserName)/posts/$($post.slug)" -Lang 'ru'
Check 'Public detail (anonim) ochiladi' ($public.Status -eq 200 -and $public.Data.title -eq $post.title) $public.Raw
Check 'Public detail''da kategoriya nomi joriy tilda (ru)' ($public.Raw -match 'Программирование') ''
Check 'Autosave e''lon qilingan matnni o''zgartirmagan' ($public.Raw -notmatch 'Autosave matni') ''
$search = Invoke-Api GET "/api/public/posts?q=ajoyibso%27z"
$search2 = Invoke-Api GET "/api/public/posts?q=ajoyibso"
Check 'Full-text qidiruv matndagi so''z bo''yicha topadi' ((@($search.Data.items).Count + @($search2.Data.items).Count) -ge 1) ($search.Raw + ' | ' + $search2.Raw)
$authors = Invoke-Api GET "/api/public/authors/$($alice.UserName)"
Check 'Public muallif profili (skill''lar bilan)' ($authors.Status -eq 200 -and @($authors.Data.skills).Count -eq 2) $authors.Raw
$authorCats = Invoke-Api GET "/api/public/authors/$($alice.UserName)/categories" -Lang 'en'
Check 'Public kategoriya daraxti en tilida, post soni bilan' ($authorCats.Raw -match 'Programming') $authorCats.Raw

Write-Host "`n== Izohlar va reaksiyalar ==" -ForegroundColor Cyan
$anonComment = Invoke-Api POST "/api/posts/$($post.id)/comments" @{ content = 'Anonim' }
Check 'Anonim izoh yozolmaydi (401)' ($anonComment.Status -eq 401) $anonComment.Raw
$c1 = Invoke-Api POST "/api/posts/$($post.id)/comments" @{ content = 'Zo''r maqola!' } -Token $bob.Token
Check 'Bob izoh yozdi' ($c1.Status -eq 201 -and $c1.Data.author.username -eq $bob.UserName) $c1.Raw
$c2 = Invoke-Api POST "/api/posts/$($post.id)/comments" @{ content = 'Rahmat!'; parentId = $c1.Data.id } -Token $alice.Token
Check 'Alice javob yozdi (depth 1)' ($c2.Status -eq 201 -and $c2.Data.depth -eq 1) $c2.Raw
$edit = Invoke-Api PUT "/api/comments/$($c1.Data.id)" @{ content = 'Tahrir' } -Token $alice.Token
Check 'Alice Bob izohini tahrirlay olmaydi' ($edit.Status -eq 403 -or $edit.Status -eq 404) $edit.Raw
$like = Invoke-Api PUT "/api/posts/$($post.id)/reaction" @{ type = 'Like' } -Token $bob.Token
Check 'Bob like bosdi' ($like.Status -eq 200 -and $like.Data.likeCount -eq 1 -and $like.Data.myReaction -eq 'Like') $like.Raw
$switch = Invoke-Api PUT "/api/posts/$($post.id)/reaction" @{ type = 'Dislike' } -Token $bob.Token
Check 'Like → Dislike: hisoblagichlar to''g''ri' ($switch.Data.likeCount -eq 0 -and $switch.Data.dislikeCount -eq 1) $switch.Raw
$cLike = Invoke-Api PUT "/api/comments/$($c1.Data.id)/reaction" @{ type = 'Like' } -Token $alice.Token
Check 'Izohga like' ($cLike.Status -eq 200 -and $cLike.Data.likeCount -eq 1) $cLike.Raw
$list = Invoke-Api GET "/api/posts/$($post.id)/comments" -Token $bob.Token
$root = @($list.Data.items)[0]
Check 'Izohlar daraxti: 1 root + 1 javob, muallif ko''rinadi' (@($list.Data.items).Count -eq 1 -and @($root.replies).Count -eq 1 -and $root.author.username -eq $bob.UserName) $list.Raw
$after = Invoke-Api GET "/api/public/authors/$($alice.UserName)/posts/$($post.slug)" -Token $bob.Token
Check 'Public post: commentCount=2, myReaction=Dislike' ($after.Raw -match '"comments":2' -and $after.Data.myReaction -eq 'Dislike') $after.Raw
$notify = Get-MailFor $alice.Email $post.title
Check 'Alice''ga yangi izoh haqida email keldi' ($null -ne $notify)

Write-Host "`n== Parolni tiklash ==" -ForegroundColor Cyan
$forgot = Invoke-Api POST '/api/auth/forgot-password' @{ email = $alice.Email }
$forgotUnknown = Invoke-Api POST '/api/auth/forgot-password' @{ email = "nobody.$run@e2e.local" }
Check 'Forgot password: mavjud va mavjud bo''lmagan email uchun bir xil javob' ($forgot.Status -eq 200 -and $forgot.Raw -eq $forgotUnknown.Raw) ($forgot.Raw + ' | ' + $forgotUnknown.Raw)
$resetMail = Get-MailFor $alice.Email 'reset-password'
Check 'Tiklash xati keldi' ($null -ne $resetMail)
$rq = Get-QueryFromMail $resetMail 'reset-password'
$newPassword = 'NewPassw0rd!' + $run
$reset = Invoke-Api POST '/api/auth/reset-password' @{ email = $rq['email']; token = $rq['token']; newPassword = $newPassword; confirmPassword = $newPassword }
Check 'Parol tiklandi' ($reset.Status -eq 204) $reset.Raw
$oldRefresh = Invoke-Api POST '/api/auth/refresh' @{ refreshToken = $alice.Refresh }
Check 'Tiklashdan keyin eski refresh token bekor' ($oldRefresh.Status -eq 401) $oldRefresh.Raw
$oldLogin = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $alice.Email; password = $alice.Password }
$newLogin = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $alice.Email; password = $newPassword }
Check 'Eski parol ishlamaydi, yangisi ishlaydi' ($oldLogin.Status -ge 400 -and $newLogin.Status -eq 200) ($oldLogin.Raw + ' | ' + $newLogin.Raw)

Write-Host "`n== Admin ==" -ForegroundColor Cyan
$admin = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = 'superadmin'; password = 'ChangeMe123!' }
Check 'SuperAdmin login' ($admin.Status -eq 200) $admin.Raw
$users = Invoke-Api GET "/api/admin/users?search=$run" -Token $admin.Data.accessToken
Check 'Admin userlar ro''yxatini ko''radi' ($users.Status -eq 200 -and @($users.Data.items).Count -ge 2) $users.Raw
$bobAdmin = Invoke-Api GET '/api/admin/users' -Token $bob.Token
Check 'Oddiy user admin endpoint''iga kira olmaydi (403)' ($bobAdmin.Status -eq 403 -and $bobAdmin.Data.code -eq 'General.Forbidden') $bobAdmin.Raw
$block = Invoke-Api POST "/api/admin/users/$($bob.Id)/block" -Token $admin.Data.accessToken
$bobLogin = Invoke-Api POST '/api/auth/login' @{ emailOrUserName = $bob.Email; password = $bob.Password }
Check 'Bloklangan user login qila olmaydi' ($block.Status -eq 204 -and $bobLogin.Status -ge 400) ($block.Raw + ' | ' + $bobLogin.Raw)

Write-Host ""
Write-Host "Natija: $($script:passed) ta OK, $($script:failed) ta FAIL" -ForegroundColor $(if ($script:failed -eq 0) { 'Green' } else { 'Red' })
exit $script:failed
