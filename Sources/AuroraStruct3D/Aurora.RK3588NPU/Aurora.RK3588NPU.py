from rk3588_env import RK3588_ENV


def main() -> None:
    print(f"SSH target: {RK3588_ENV.ssh_target}")
    print(f"Model: {RK3588_ENV.model_name}")
    print(f"Dashboard: {RK3588_ENV.dashboard_url}")
    print(f"Generate API: {RK3588_ENV.generate_url}")


if __name__ == "__main__":
    main()
