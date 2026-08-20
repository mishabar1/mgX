using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using MG.Server.Services;

// NOTE (.NET 10 upgrade): TensorFlow.NET/Keras packages were removed (large native dependency,
// used only by the experimental TensofFlowTest below). The usings and method body are preserved
// as comments. To restore, re-add the SciSharp TensorFlow packages in MG.Server.csproj and
// un-comment both this block and the method body.
// using static Tensorflow.Binding;
// using static Tensorflow.KerasApi;
// using Tensorflow;
// using Tensorflow.NumPy;

namespace MG.Server.BL
{
    public class LoginResult
    {
        public string Token { get; set; } = string.Empty;
        public UserData User { get; set; } = null!;
    }

    public class UserBL
    {
        DataRepository _dataRepository;
        TokenService _tokenService;
        public UserBL(DataRepository dataRepository, TokenService tokenService)
        {
            _dataRepository = dataRepository;
            _tokenService = tokenService;
        }

        internal async Task<LoginResult?> Login(LoginData data)
        {
            // A blank name would create an anonymous, unaddressable account (and every later
            // blank login would collide with it). Refuse it here; the controller turns null
            // into a 400.
            if (string.IsNullOrWhiteSpace(data?.name)) return null;

            // Exact match — "A" and "a" are different users on purpose.
            var user = _dataRepository.Users.FindLast(x => x.Name == data.name);

            var derivedId = UserData.IdForName(data.name);

            if (user != null && user.Id != derivedId)
            {
                // An account created before ids were derived from the name. Adopt the derived id
                // so the invariant "id == f(name)" holds for EVERY account, not just new ones —
                // otherwise behaviour would depend on when you first signed up.
                //
                // This orphans that user's references in already-saved games (CreatorId,
                // seat.User.Id). Accepted deliberately: this is POC data, the store already lives
                // in the OS temp folder, and a one-off break now beats an id scheme with two
                // permanent classes of user in it.
                Console.WriteLine($"Login: re-issuing id for '{data.name}': {user.Id} -> {derivedId}");
                user.Id = derivedId;
            }

            if (user == null)
            {
                // The id is DERIVED FROM THE NAME (UserData.IdForName), not random. That is what
                // makes logging back in as "A" land on the same account: it no longer depends on
                // the users list having survived, so a wiped SQLite file, a restart, or a
                // redeploy can't quietly turn you into a stranger who doesn't own their own games.
                user = new UserData { Name = data.name, Id = derivedId };
                _dataRepository.Users.Add(user);
            }

            await _dataRepository.Save();

            // (C2) issue a signed JWT the client sends on subsequent API/SignalR calls.
            var token = _tokenService.CreateToken(user);

            return new LoginResult { Token = token, User = user };
        }

        internal Task TensofFlowTest()
        {
            // DISABLED during .NET 10 upgrade — TensorFlow.NET packages removed. See note at top of file.
            // Original experimental body preserved below; re-add packages to restore.
            return Task.CompletedTask;
            /*
            var layers = keras.layers;
            // input layer
            var inputs = keras.Input(shape: (32, 32, 3), name: "img");
            // convolutional layer
            var x = layers.Conv2D(32, 3, activation: "relu").Apply(inputs);
            x = layers.Conv2D(64, 3, activation: "relu").Apply(x);
            var block_1_output = layers.MaxPooling2D(3).Apply(x);
            x = layers.Conv2D(64, 3, activation: "relu", padding: "same").Apply(block_1_output);
            x = layers.Conv2D(64, 3, activation: "relu", padding: "same").Apply(x);
            var block_2_output = layers.Add().Apply(new Tensors(x, block_1_output));
            x = layers.Conv2D(64, 3, activation: "relu", padding: "same").Apply(block_2_output);
            x = layers.Conv2D(64, 3, activation: "relu", padding: "same").Apply(x);
            var block_3_output = layers.Add().Apply(new Tensors(x, block_2_output));
            x = layers.Conv2D(64, 3, activation: "relu").Apply(block_3_output);
            x = layers.GlobalAveragePooling2D().Apply(x);
            x = layers.Dense(256, activation: "relu").Apply(x);
            x = layers.Dropout(0.5f).Apply(x);
            // output layer
            var outputs = layers.Dense(10).Apply(x);
            // build keras model
            var model = keras.Model(inputs, outputs, name: "toy_resnet");
            model.summary();
            // compile keras model in tensorflow static graph
            model.compile(optimizer: keras.optimizers.RMSprop(1e-3f),
                loss: keras.losses.SparseCategoricalCrossentropy(from_logits: true),
                metrics: new[] { "acc" });
            // prepare dataset
            var ((x_train, y_train), (x_test, y_test)) = keras.datasets.cifar10.load_data();
            // normalize the input
            x_train = x_train / 255.0f;
            // training
            model.fit(x_train[new Slice(0, 2000)], y_train[new Slice(0, 2000)],
                        batch_size: 64,
                        epochs: 10,
                        validation_split: 0.2f);
            // save the model
            model.save("./toy_resnet_model");


            //// Parameters        
            //var training_steps = 10000;
            //var learning_rate = 0.01f;
            //var display_step = 100;

            //// Sample data
            //var X = np.array(3.3f, 4.4f, 5.5f, 6.71f, 6.93f, 4.168f, 9.779f, 6.182f, 7.59f, 2.167f,
            //             7.042f, 10.791f, 5.313f, 7.997f, 5.654f, 9.27f, 3.1f);
            //var Y = np.array(1.7f, 2.76f, 2.09f, 3.19f, 1.694f, 1.573f, 3.366f, 2.596f, 2.53f, 1.221f,
            //             2.827f, 3.465f, 1.65f, 2.904f, 2.42f, 2.94f, 1.3f);
            //var n_samples = X.shape[0];

            //// We can set a fixed init value in order to demo
            //var W = tf.Variable(-0.06f, name: "weight");
            //var b = tf.Variable(-0.73f, name: "bias");
            //var optimizer = keras.optimizers.Adam(learning_rate);

            //// Run training for the given number of steps.
            //foreach (var step in range(1, training_steps + 1))
            //{
            //    // Run the optimization to update W and b values.
            //    // Wrap computation inside a GradientTape for automatic differentiation.
            //    using var g = tf.GradientTape();
            //    // Linear regression (Wx + b).
            //    var pred = W * X + b;
            //    // Mean square error.
            //    var loss = tf.reduce_sum(tf.pow(pred - Y, 2)) / (2 * n_samples);
            //    // should stop recording
            //    // Compute gradients.
            //    var gradients = g.gradient(loss, (W, b));

            //    // Update W and b following gradients.
            //    optimizer.apply_gradients(zip(gradients, (W, b)));

            //    if (step % display_step == 0)
            //    {
            //        pred = W * X + b;
            //        loss = tf.reduce_sum(tf.pow(pred - Y, 2)) / (2 * n_samples);
            //        print($"step: {step}, loss: {loss.numpy()}, W: {W.numpy()}, b: {b.numpy()}");
            //    }
            //}
            */
        }
    }
}
