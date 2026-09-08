// What the shell was told to do, resolved once at startup from the command line and the
// environment before anything else runs.
//
// Precedence, highest first: `--server-url <url>` / `--data-directory <path>` on the command line,
// then `NEKTO_SERVER_URL` / `NEKTO_DATA_DIRECTORY`. With neither given, a debug build attaches to
// the Vite dev server a developer already has running on 5173 (which proxies to the API on 5080);
// a release build spawns its own server instead.
use std::env;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ShellMode {
    // Nothing is started or stopped by this process - only polled and shown.
    Attach { url: String },
    // Started as a child of this process. `data_directory`, when given, is passed straight
    // through to it as `--DataDirectory` so the shell and the server can never disagree about
    // where the library lives.
    Spawn { data_directory: Option<String> },
}

const DEV_SERVER_URL: &str = "http://127.0.0.1:5173";

pub fn resolve() -> ShellMode {
    resolve_from(env::args().skip(1), |name| env::var(name).ok())
}

// Takes its inputs as parameters, rather than reading argv and the environment itself, so a test
// can drive every branch of the precedence above without touching the real process's own.
pub fn resolve_from<Args, EnvVar>(args: Args, env_var: EnvVar) -> ShellMode
where
    Args: IntoIterator<Item = String>,
    EnvVar: Fn(&str) -> Option<String>,
{
    let (arg_server_url, arg_data_directory) = parse_args(args);

    let server_url = arg_server_url.or_else(|| env_var("NEKTO_SERVER_URL"));
    let data_directory = arg_data_directory.or_else(|| env_var("NEKTO_DATA_DIRECTORY"));

    if let Some(url) = server_url {
        return ShellMode::Attach { url };
    }

    // A data directory only means anything to a spawned server, so giving one without a server
    // url is read as asking for spawn mode even in a debug build - otherwise there would be no
    // way to exercise the real spawn path from `cargo tauri dev` short of a full release build,
    // which is also why server.rs still knows where to find the binary under `src-tauri/server`
    // for exactly this case.
    if data_directory.is_some() {
        return ShellMode::Spawn { data_directory };
    }

    if cfg!(debug_assertions) {
        return ShellMode::Attach {
            url: DEV_SERVER_URL.to_string(),
        };
    }

    ShellMode::Spawn {
        data_directory: None,
    }
}

fn parse_args<Args: IntoIterator<Item = String>>(args: Args) -> (Option<String>, Option<String>) {
    let mut server_url = None;
    let mut data_directory = None;
    let mut iterator = args.into_iter();

    while let Some(arg) = iterator.next() {
        if let Some(value) = arg.strip_prefix("--server-url=") {
            server_url = Some(value.to_string());
        } else if arg == "--server-url" {
            server_url = iterator.next();
        } else if let Some(value) = arg.strip_prefix("--data-directory=") {
            data_directory = Some(value.to_string());
        } else if arg == "--data-directory" {
            data_directory = iterator.next();
        }
    }

    (server_url, data_directory)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn no_env(_name: &str) -> Option<String> {
        None
    }

    fn args(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| value.to_string()).collect()
    }

    #[test]
    fn a_command_line_server_url_wins_over_everything_else() {
        let mode = resolve_from(
            args(&[
                "--server-url",
                "http://127.0.0.1:9001",
                "--data-directory",
                "ignored",
            ]),
            |name| {
                if name == "NEKTO_SERVER_URL" {
                    Some("http://127.0.0.1:9002".to_string())
                } else {
                    None
                }
            },
        );

        assert_eq!(
            mode,
            ShellMode::Attach {
                url: "http://127.0.0.1:9001".to_string()
            }
        );
    }

    #[test]
    fn the_equals_form_of_server_url_is_accepted() {
        let mode = resolve_from(args(&["--server-url=http://127.0.0.1:9003"]), no_env);

        assert_eq!(
            mode,
            ShellMode::Attach {
                url: "http://127.0.0.1:9003".to_string()
            }
        );
    }

    #[test]
    fn an_environment_server_url_is_used_when_no_flag_is_given() {
        let mode = resolve_from(args(&[]), |name| {
            if name == "NEKTO_SERVER_URL" {
                Some("http://127.0.0.1:9004".to_string())
            } else {
                None
            }
        });

        assert_eq!(
            mode,
            ShellMode::Attach {
                url: "http://127.0.0.1:9004".to_string()
            }
        );
    }

    #[test]
    fn a_command_line_data_directory_forces_spawn_even_in_a_debug_build() {
        let mode = resolve_from(args(&["--data-directory", "R:\\scratch"]), no_env);

        assert_eq!(
            mode,
            ShellMode::Spawn {
                data_directory: Some("R:\\scratch".to_string())
            }
        );
    }

    #[test]
    fn an_environment_data_directory_forces_spawn_the_same_way() {
        let mode = resolve_from(args(&[]), |name| {
            if name == "NEKTO_DATA_DIRECTORY" {
                Some("R:\\scratch-env".to_string())
            } else {
                None
            }
        });

        assert_eq!(
            mode,
            ShellMode::Spawn {
                data_directory: Some("R:\\scratch-env".to_string())
            }
        );
    }

    #[test]
    fn a_command_line_flag_wins_over_the_same_setting_in_the_environment() {
        let mode = resolve_from(args(&["--data-directory", "R:\\from-arg"]), |name| {
            if name == "NEKTO_DATA_DIRECTORY" {
                Some("R:\\from-env".to_string())
            } else {
                None
            }
        });

        assert_eq!(
            mode,
            ShellMode::Spawn {
                data_directory: Some("R:\\from-arg".to_string())
            }
        );
    }

    // The only two branches that read cfg!(debug_assertions) - exercised here as whatever this
    // test binary itself was compiled as, which is a debug build under `cargo test`.
    #[test]
    fn with_nothing_given_a_debug_build_attaches_to_the_dev_server() {
        let mode = resolve_from(args(&[]), no_env);

        assert_eq!(
            mode,
            ShellMode::Attach {
                url: DEV_SERVER_URL.to_string()
            }
        );
    }
}
