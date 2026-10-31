using System.Net;

namespace UluBilge.Api.Authentication
{
    internal static class OAuthLoginPage
    {
        public static string Render(
            string clientName,
            bool allowOfflineAccess,
            int refreshTokenLifetimeDays,
            string hiddenFields,
            string? error = null)
        {
            var accessDuration = allowOfflineAccess
                ? $"En fazla {refreshTokenLifetimeDays} gün boyunca otomatik yenilenebilir."
                : "Kısa süreli erişim. Otomatik yenilenmez.";
            var errorMessage = string.IsNullOrEmpty(error)
                ? ""
                : $"<div class=\"error\" id=\"login-error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";

            return $$"""
                <!doctype html>
                <html lang="tr">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Rapor erişimi · UluBilge</title>
                    <style>
                        :root {
                            color-scheme: light;
                            font-family: "Segoe UI", system-ui, sans-serif;
                            color: #292d28;
                            background: #f5f2eb;
                            font-synthesis: none;
                            --muted: #65685f;
                            --rule: #d6d4ca;
                            --accent: #9a4d31;
                        }

                        * { box-sizing: border-box; }
                        body { margin: 0; }
                        h1, h2, p, dl, dd { margin: 0; }
                        button, input { font: inherit; }

                        .page {
                            display: flex;
                            flex-direction: column;
                            min-height: 100vh;
                            min-height: 100svh;
                            max-width: 1280px;
                            margin: 0 auto;
                            padding: 0 64px;
                        }

                        .masthead {
                            display: flex;
                            align-items: baseline;
                            justify-content: space-between;
                            gap: 24px;
                            padding: 30px 0 24px;
                            border-bottom: 1px solid var(--rule);
                        }

                        .wordmark {
                            font-size: 23px;
                            font-weight: 650;
                            letter-spacing: -.9px;
                        }

                        .wordmark span { color: var(--accent); }
                        .product-name { color: var(--muted); font-size: 13px; }

                        main {
                            width: 100%;
                            max-width: 944px;
                            margin: 64px auto 72px;
                        }

                        .page-label {
                            display: flex;
                            align-items: center;
                            gap: 10px;
                            margin-bottom: 18px;
                            color: var(--accent);
                            font-size: 13px;
                            font-weight: 600;
                        }

                        .page-label::before {
                            width: 24px;
                            height: 2px;
                            background: currentColor;
                            content: "";
                        }

                        h1 {
                            max-width: 700px;
                            font-family: Georgia, "Times New Roman", serif;
                            font-size: clamp(34px, 3.8vw, 46px);
                            font-weight: 400;
                            line-height: 1.16;
                            letter-spacing: -1.5px;
                            text-wrap: balance;
                        }

                        .page-description {
                            margin-top: 16px;
                            color: var(--muted);
                            font-size: 14px;
                            line-height: 1.7;
                        }

                        .authorization {
                            display: grid;
                            grid-template-columns: 1fr 1fr;
                            margin-top: 36px;
                            border-top: 2px solid #363d34;
                            border-bottom: 1px solid var(--rule);
                        }

                        .request {
                            min-width: 0;
                            padding: 32px 48px 36px 0;
                            border-right: 1px solid var(--rule);
                        }

                        .section-label { color: var(--muted); font-size: 12px; }

                        .client-name {
                            margin-top: 10px;
                            font-size: 25px;
                            font-weight: 600;
                            line-height: 1.35;
                            letter-spacing: -.6px;
                            overflow-wrap: anywhere;
                        }

                        .request-description {
                            max-width: 340px;
                            margin-top: 12px;
                            font-size: 14px;
                            line-height: 1.75;
                        }

                        .permissions { margin-top: 28px; }

                        .permission {
                            display: grid;
                            grid-template-columns: 104px minmax(0, 1fr);
                            gap: 16px;
                            padding: 16px 0;
                            border-top: 1px solid var(--rule);
                            font-size: 13px;
                            line-height: 1.65;
                        }

                        dt { color: var(--muted); }

                        .request-note {
                            max-width: 340px;
                            margin-top: 24px;
                            color: var(--muted);
                            font-size: 12px;
                            line-height: 1.75;
                        }

                        .sign-in { min-width: 0; padding: 32px 0 36px 48px; }
                        .sign-in h2 { font-size: 18px; font-weight: 600; letter-spacing: -.25px; }
                        .sign-in-description { margin-top: 8px; color: var(--muted); font-size: 13px; line-height: 1.7; }
                        form { margin-top: 26px; }
                        .field + .field { margin-top: 20px; }
                        label { display: block; margin-bottom: 9px; font-size: 13px; font-weight: 600; }

                        input {
                            width: 100%;
                            min-height: 48px;
                            padding: 11px 12px;
                            border: 1px solid #aaafa3;
                            border-radius: 3px;
                            color: #292d28;
                            background: #fffefa;
                            font-size: 16px;
                        }

                        input:hover { border-color: #747a6f; }
                        input:focus-visible { border-color: var(--accent); outline: 2px solid var(--accent); outline-offset: 2px; }

                        .error {
                            margin-top: 22px;
                            padding: 12px 14px;
                            border-left: 3px solid #a13024;
                            color: #8c271e;
                            background: #f6e8e1;
                            font-size: 13px;
                            line-height: 1.7;
                            overflow-wrap: anywhere;
                        }

                        .actions {
                            display: flex;
                            align-items: center;
                            gap: 12px;
                            margin-top: 28px;
                        }

                        button {
                            min-height: 48px;
                            padding: 12px 18px;
                            border: 1px solid transparent;
                            border-radius: 3px;
                            font-size: 13px;
                            font-weight: 600;
                            line-height: 1.5;
                            cursor: pointer;
                        }

                        button:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
                        .allow { flex: 1; color: #fffefa; background: #343d33; }
                        .allow:hover { background: #232c23; }
                        .deny { color: #464c43; background: transparent; }
                        .deny:hover { border-color: #bebfb3; background: #ebe9e0; }
                        .form-note { margin-top: 18px; color: var(--muted); font-size: 12px; line-height: 1.75; }

                        .page-footer {
                            display: flex;
                            justify-content: space-between;
                            gap: 16px;
                            margin-top: auto;
                            padding: 20px 0 24px;
                            color: var(--muted);
                            font-size: 11px;
                            line-height: 1.6;
                        }

                        @media (max-width: 900px) {
                            .page { padding: 0 32px; }
                            main { margin-top: 48px; }
                            .request { padding-right: 32px; }
                            .sign-in { padding-left: 32px; }
                            .permission { grid-template-columns: 88px minmax(0, 1fr); gap: 12px; }
                            .actions { gap: 6px; }
                            button { padding-right: 12px; padding-left: 12px; }
                        }

                        @media (max-width: 680px) {
                            .page { padding: 0 24px; }
                            .masthead { padding: 24px 0 20px; }
                            .wordmark { font-size: 21px; }
                            main { margin: 36px auto 32px; }
                            h1 { letter-spacing: -1px; }
                            .page-description { max-width: 320px; }
                            .authorization { grid-template-columns: minmax(0, 1fr); margin-top: 28px; }
                            .request { padding: 24px 0; border-right: 0; border-bottom: 1px solid var(--rule); }
                            .client-name { font-size: 23px; }
                            .permissions { margin-top: 20px; }
                            .permission { grid-template-columns: 96px minmax(0, 1fr); padding: 12px 0; }
                            .request-note { margin-top: 16px; }
                            .sign-in { padding: 28px 0; }
                            .actions { gap: 12px; }
                        }

                        @media (forced-colors: active) {
                            input, button, .error { border: 1px solid ButtonText; }
                        }
                    </style>
                </head>
                <body>
                    <div class="page">
                        <header class="masthead">
                            <div class="wordmark">UluBilge<span aria-hidden="true">.</span></div>
                            <p class="product-name">Raporlama</p>
                        </header>

                        <main aria-labelledby="page-title">
                            <p class="page-label">Uygulama bağlantısı</p>
                            <h1 id="page-title">Rapor erişimini onaylayın.</h1>
                            <p class="page-description">Bir uygulamayı UluBilge hesabınıza bağlıyorsunuz.</p>

                            <div class="authorization">
                                <section class="request" aria-labelledby="client-name" id="access-summary">
                                    <p class="section-label">Erişim isteyen uygulama</p>
                                    <h2 class="client-name" id="client-name">{{WebUtility.HtmlEncode(clientName)}}</h2>
                                    <p class="request-description">UluBilge hesabınızdaki raporları okumak istiyor.</p>

                                    <dl class="permissions">
                                        <div class="permission">
                                            <dt>İstenen izin</dt>
                                            <dd>Raporları okuma</dd>
                                        </div>
                                        <div class="permission">
                                            <dt>Erişim süresi</dt>
                                            <dd>{{accessDuration}}</dd>
                                        </div>
                                    </dl>

                                    <p class="request-note">Bu bağlantıyı siz başlatmadıysanız izin vermeden iptal edebilirsiniz.</p>
                                </section>

                                <section class="sign-in" aria-labelledby="sign-in-title">
                                    <h2 id="sign-in-title">Hesabınızı doğrulayın</h2>
                                    <p class="sign-in-description">UluBilge kullanıcı adınızı ve parolanızı girin.</p>
                                    {{errorMessage}}

                                    <form method="post" action="/oauth/login" aria-describedby="access-summary{{(string.IsNullOrEmpty(error) ? "" : " login-error")}}">
                                        {{hiddenFields}}
                                        <div class="field">
                                            <label for="userName">Kullanıcı adı</label>
                                            <input id="userName" name="userName" maxlength="64" autocomplete="username" autocapitalize="none" spellcheck="false" required>
                                        </div>
                                        <div class="field">
                                            <label for="password">Parola</label>
                                            <input id="password" type="password" name="password" maxlength="128" autocomplete="current-password" required>
                                        </div>
                                        <div class="actions">
                                            <button class="allow" type="submit" name="decision" value="allow">Giriş yap ve izin ver</button>
                                            <button class="deny" type="submit" name="decision" value="deny" formnovalidate>İptal et</button>
                                        </div>
                                        <p class="form-note">Giriş yaptığınızda, belirtilen uygulamaya yukarıdaki erişim için izin vermiş olursunuz.</p>
                                    </form>
                                </section>
                            </div>
                        </main>

                        <footer class="page-footer">
                            <span>UluBilge</span>
                            <span>Hesap bağlantıları</span>
                        </footer>
                    </div>
                </body>
                </html>
                """;
        }
    }
}
