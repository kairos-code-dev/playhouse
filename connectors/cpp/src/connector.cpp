#include "playhouse/connector.hpp"
#include "client_network.hpp"
#include <stdexcept>

// Additional ASIO headers for implementation
#if defined(ASIO_STANDALONE) || !defined(BOOST_ASIO_HPP)
    #include <asio/co_spawn.hpp>
    #include <asio/detached.hpp>
    #include <asio/use_awaitable.hpp>
#else
    #include <boost/asio/co_spawn.hpp>
    #include <boost/asio/detached.hpp>
    #include <boost/asio/use_awaitable.hpp>
#endif

namespace playhouse {

class Connector::Impl {
public:
    ConnectorConfig config_;
    std::unique_ptr<internal::ClientNetwork> network_;
    int64_t stage_id_;
    bool initialized_;

    Impl()
        : stage_id_(0)
        , initialized_(false)
    {}

    void EnsureInitialized() const {
        if (!initialized_) {
            throw std::runtime_error("Connector not initialized. Call Init() first.");
        }
    }
};

Connector::Connector()
    : impl_(std::make_unique<Impl>())
{}

Connector::~Connector() = default;

void Connector::Init(const ConnectorConfig& config) {
    impl_->config_ = config;
    impl_->network_ = std::make_unique<internal::ClientNetwork>(config);
    impl_->initialized_ = true;

    // Set up callbacks forwarding
    impl_->network_->SetOnConnect([this](bool success) {
        if (success) {
            if (OnConnect) {
                OnConnect();
            }
        } else {
            if (OnError) {
                OnError(error_code::CONNECTION_FAILED, "Failed to connect to server");
            }
        }
    });

    impl_->network_->SetOnReceive([this](Packet packet) {
        if (OnReceive) {
            OnReceive(std::move(packet));
        }
    });

    impl_->network_->SetOnError([this](int code, std::string message) {
        if (OnError) {
            OnError(code, std::move(message));
        }
    });

    impl_->network_->SetOnDisconnect([this]() {
        if (OnDisconnect) {
            OnDisconnect();
        }
    });
}

// C++20 Coroutine version
asio::awaitable<bool> Connector::Connect(const std::string& host, uint16_t port) {
    impl_->EnsureInitialized();
    co_return co_await impl_->network_->Connect(host, port);
}

// Legacy std::future version (deprecated)
std::future<bool> Connector::ConnectAsync(const std::string& host, uint16_t port) {
    impl_->EnsureInitialized();
    return impl_->network_->ConnectAsync(host, port);
}

void Connector::Disconnect() {
    if (impl_->initialized_) {
        impl_->stage_id_ = 0;
        impl_->network_->Disconnect();
    }
}

bool Connector::IsConnected() const {
    if (!impl_->initialized_) {
        return false;
    }
    return impl_->network_->IsConnected();
}

void Connector::Send(Packet packet) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        if (OnError) {
            OnError(error_code::CONNECTION_CLOSED, "Not connected");
        }
        return;
    }

    impl_->network_->Send(std::move(packet), impl_->stage_id_);
}

// C++20 Coroutine version
asio::awaitable<Packet> Connector::Request(Packet packet) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        throw std::runtime_error("Not connected");
    }

    co_return co_await impl_->network_->Request(std::move(packet), impl_->stage_id_);
}

// Callback overload version
void Connector::Request(Packet packet, std::function<void(Packet)> callback) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        if (OnError) {
            OnError(error_code::CONNECTION_CLOSED, "Not connected");
        }
        return;
    }

    impl_->network_->Request(std::move(packet), std::move(callback), impl_->stage_id_);
}

// Legacy std::future version (deprecated)
std::future<Packet> Connector::RequestAsync(Packet packet) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        auto promise = std::promise<Packet>();
        promise.set_exception(std::make_exception_ptr(
            std::runtime_error("Not connected")
        ));
        return promise.get_future();
    }

    return impl_->network_->RequestAsync(std::move(packet), impl_->stage_id_);
}

// C++20 Coroutine version
asio::awaitable<bool> Connector::Authenticate(Packet authPacket) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        throw std::runtime_error("Not connected");
    }

    Packet response = co_await impl_->network_->Authenticate(std::move(authPacket), 0);
    const bool success = (response.GetErrorCode() == 0);
    if (success) {
        impl_->stage_id_ = response.GetStageId();
    }
    co_return success;
}

// Callback overload version
void Connector::Authenticate(Packet authPacket, std::function<void(bool)> callback) {
    impl_->EnsureInitialized();

    if (!IsConnected()) {
        if (OnError) {
            OnError(error_code::CONNECTION_CLOSED, "Not connected");
        }
        return;
    }

    impl_->network_->Authenticate(
        std::move(authPacket),
        [this, callback](Packet response) {
            bool success = (response.GetErrorCode() == 0);
            if (success) {
                impl_->stage_id_ = response.GetStageId();
            }
            callback(success);
        },
        0
    );
}

// Legacy std::future version (deprecated)
std::future<bool> Connector::AuthenticateAsync(
    const std::string& service_id,
    const std::string& account_id,
    const Bytes& payload)
{
    // Silence unused parameter warnings for deprecated method
    (void)service_id;
    (void)account_id;

    impl_->EnsureInitialized();

    if (!IsConnected()) {
        auto promise = std::promise<bool>();
        promise.set_exception(std::make_exception_ptr(
            std::runtime_error("Not connected")
        ));
        return promise.get_future();
    }

    // Create authentication packet
    // Note: The actual message ID and format depend on your authentication protocol
    // This is a placeholder implementation
    Packet auth_packet("Authenticate", payload);

    auto promise = std::make_shared<std::promise<bool>>();
    auto future = promise->get_future();

    impl_->network_->Authenticate(
        std::move(auth_packet),
        [this, promise](Packet response) {
            bool success = (response.GetErrorCode() == 0);
            if (success) {
                impl_->stage_id_ = response.GetStageId();
            }
            promise->set_value(success);
        },
        0
    );

    return future;
}

void Connector::MainThreadAction() {
    if (impl_->initialized_) {
        impl_->network_->MainThreadAction();
    }
}

} // namespace playhouse
