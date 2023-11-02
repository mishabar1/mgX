using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;

using static Tensorflow.Binding;
using static Tensorflow.KerasApi;
using Tensorflow;
using Tensorflow.NumPy;

namespace MG.Server.BL
{
    public class UserBL
    {
        DataRepository _dataRepository;
        public UserBL(DataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        internal async Task<UserData> Login(LoginData data)
        {

           var user = _dataRepository.Users.FindLast(x => x.Name == data.name);
            if (user == null)
            {
                user = new UserData() { Name = data.name };
                _dataRepository.Users.Add(user);
            }

            _dataRepository.Save();

            return user;

        }

        internal async Task TensofFlowTest()
        {
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

        }
    }
}
